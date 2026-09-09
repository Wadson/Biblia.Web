using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Biblia.Application.Interfaces;
using Biblia.Infrastructure.BibleDatabases;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AppUserDatabase=Biblia.Infrastructure.AppDatabase.AppDatabase;
namespace Biblia.Infrastructure.Files;

public sealed class BackupService(IAppDatabase database,IAppPaths paths,ILogger<BackupService> logger):IBackupService
{
    public const int MaximumVersions=128;
    public const long MaximumBibleSize=256L*1024*1024,MaximumTotalSize=8L*1024*1024*1024;
    private readonly BibleValidationService validator=new(NullLogger<BibleValidationService>.Instance);
    public sealed record BibleFile(string Code,string DisplayName,string EntryName,long Size,string Sha256,int SchemaVersion,string Language,bool IsEnabled,bool IsActive,bool IsBundled,int ValidationStatus);
    public sealed record Manifest(DateTimeOffset CreatedAt,int SchemaVersion,string AppVersion="1.0.0",long DatabaseSize=0,string? DatabaseSha256=null,int BackupFormatVersion=1,IReadOnlyList<BibleFile>? Bibles=null);
    public async Task<BackupInfo> CreateAsync(CancellationToken cancellationToken=default)
    {
        using var lease=await FileOperationLease.AcquireAsync(paths.AppDataDirectory,cancellationToken);
        return await CreateCore(cancellationToken);
    }
    private async Task<BackupInfo> CreateCore(CancellationToken ct)
    {
        await database.InitializeAsync(ct);var dir=Path.Combine(paths.AppDataDirectory,"backups");Directory.CreateDirectory(dir);
        var now=DateTimeOffset.UtcNow;var path=Path.Combine(dir,$"bibliatema-{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.zip");var stage=Stage();
        try
        {
            var snapshot=Path.Combine(stage,"bibliatema.db");await Snapshot(database.DatabasePath,snapshot,ct);var schema=await ValidateDatabase(snapshot,ct);
            await using var db=await Open(snapshot,true,ct);var candidates=new List<(string Code,string Name,string Path,int Schema,string Language,bool Enabled,bool Bundled,int Status)>();
            string? active;
            await using(var cmd=db.CreateCommand()){cmd.CommandText="SELECT Value FROM Setting WHERE Key='Bible.ActiveVersionCode';";active=Convert.ToString(await cmd.ExecuteScalarAsync(ct));}
            await using(var cmd=db.CreateCommand())
            {
                cmd.CommandText="SELECT Code,DisplayName,InstalledPath,SchemaVersion,Language,IsEnabled,IsBundled,ValidationStatus FROM BibleVersionCatalog WHERE IsInstalled=1 ORDER BY Code;";
                await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))candidates.Add((r.GetString(0),r.GetString(1),r.IsDBNull(2)?"":r.GetString(2),r.GetInt32(3),r.GetString(4),r.GetInt32(5)!=0,r.GetInt32(6)!=0,r.GetInt32(7)));
            }
            if(candidates.Count>MaximumVersions)throw new InvalidDataException("Limite de 128 versões excedido.");
            var bibles=new List<BibleFile>();long total=new FileInfo(snapshot).Length;
            using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
            {
                await Add(zip,"bibliatema.db",snapshot,ct);
                foreach(var v in candidates)
                {
                    ct.ThrowIfCancellationRequested();SafeCode(v.Code);
                    if(!File.Exists(v.Path))throw new InvalidDataException($"Arquivo da versão instalada {v.Code} ausente. Corrija o catálogo antes do backup.");
                    await ValidateBible(v.Path,ct);var size=new FileInfo(v.Path).Length;total+=size;
                    if(size>MaximumBibleSize||total>MaximumTotalSize)throw new InvalidDataException("Limite de tamanho excedido.");
                    var entry="bibles/"+v.Code+".sqlite";
                    await using var input=new FileStream(v.Path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);
                    var hash=Convert.ToHexString(await SHA256.HashDataAsync(input,ct));input.Position=0;
                    await using(var output=zip.CreateEntry(entry,CompressionLevel.Optimal).Open())await input.CopyToAsync(output,ct);
                    bibles.Add(new(v.Code,v.Name,entry,size,hash,v.Schema,v.Language,v.Enabled,string.Equals(active,v.Code,StringComparison.OrdinalIgnoreCase),v.Bundled,v.Status));
                }
                CheckActive(bibles);
                var m=new Manifest(now,schema,DatabaseSize:new FileInfo(snapshot).Length,DatabaseSha256:await Hash(snapshot,ct),BackupFormatVersion:2,Bibles:bibles);
                await using var outputManifest=zip.CreateEntry("manifest.json").Open();await JsonSerializer.SerializeAsync(outputManifest,m,cancellationToken:ct);
            }
            logger.LogInformation("Backup completo criado: {Path}, {Count} versões",path,bibles.Count);return new(path,now,schema,new FileInfo(path).Length,2);
        }
        catch{if(File.Exists(path))File.Delete(path);throw;}
        finally{Directory.Delete(stage,true);}
    }
    public async Task<BackupInfo> ValidateAsync(string backupPath,CancellationToken cancellationToken=default)
    {
        var stage=Stage();try{var m=await ExtractValidate(backupPath,stage,cancellationToken);return new(backupPath,m.CreatedAt,m.SchemaVersion,new FileInfo(backupPath).Length,m.BackupFormatVersion);}finally{Directory.Delete(stage,true);}
    }
    public async Task RestoreAsync(string backupPath,CancellationToken cancellationToken=default)
    {
        using var lease=await FileOperationLease.AcquireAsync(paths.AppDataDirectory,cancellationToken);
        var stage=Stage();string? generation=null,rollback=null;bool switched=false;
        try
        {
            var m=await ExtractValidate(backupPath,stage,cancellationToken);
            var safety=await CreateCore(cancellationToken);
            rollback=Path.Combine(stage,"rollback.db");await Snapshot(database.DatabasePath,rollback,cancellationToken);
            var staged=Path.Combine(stage,"bibliatema.db");await new AppUserDatabase(staged,NullLogger<AppUserDatabase>.Instance).InitializeAsync(cancellationToken);
            if(m.BackupFormatVersion==2)
            {
                generation=Path.Combine(paths.AppDataDirectory,"Bibles","Restored-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(generation);
                foreach(var b in m.Bibles??[])
                {
                    var dest=Path.Combine(generation,b.Code+".sqlite");File.Move(Path.Combine(stage,b.EntryName.Replace('/',Path.DirectorySeparatorChar)),dest);await ValidateBible(dest,cancellationToken);
                }
            }
            await Reconcile(staged,m,generation,rollback,cancellationToken);await ValidateDatabase(staged,cancellationToken);
            // Immutable version paths + atomic SQLite destination transaction publish one coherent state.
            switched=true;await database.ReplaceAsync(staged,cancellationToken);
            await ValidateQueries(database.DatabasePath,cancellationToken);
            logger.LogInformation("Restauração confirmada; backup de segurança {Safety}; formato {Format}. Arquivos anteriores preservados para recuperação.",safety.Path,m.BackupFormatVersion);
        }
        catch(Exception ex)
        {
            if(switched&&rollback is not null)await database.ReplaceAsync(rollback,CancellationToken.None);
            if(generation is not null&&Directory.Exists(generation))Directory.Delete(generation,true);
            logger.LogError(ex,"Restauração falhou; estado anterior preservado.");throw;
        }
        finally{Directory.Delete(stage,true);}
    }
    private async Task<Manifest> ExtractValidate(string path,string stage,CancellationToken ct)
    {
        await using var input=new FileStream(Path.GetFullPath(path),FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);using var zip=new ZipArchive(input,ZipArchiveMode.Read);
        if(zip.Entries.Count>MaximumVersions+2)throw new InvalidDataException("Quantidade de arquivos excedida.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var e in zip.Entries)
        {
            var n=e.FullName;
            if(n.Contains("..")||n.Contains('\\')||n.Contains(':')||n.StartsWith('/')||!names.Add(n))throw new InvalidDataException("Backup contém caminho inseguro ou entrada duplicada.");
            total+=e.Length;var max=n=="manifest.json"?1_000_000:n=="bibliatema.db"?250_000_000:MaximumBibleSize;
            if(e.Length<=0||e.Length>max||total>MaximumTotalSize||e.Length>Math.Max(1,e.CompressedLength)*500L)throw new InvalidDataException("Tamanho ou compressão inválidos.");
        }
        var me=zip.GetEntry("manifest.json")??throw new InvalidDataException("Backup sem manifesto.");
        _=zip.GetEntry("bibliatema.db")??throw new InvalidDataException("Backup sem banco do usuário.");
        Manifest m;await using(var s=me.Open())m=await JsonSerializer.DeserializeAsync<Manifest>(s,cancellationToken:ct)??throw new InvalidDataException("Manifesto inválido.");
        if(m.BackupFormatVersion is not (1 or 2)||m.SchemaVersion<=0||m.SchemaVersion>AppUserDatabase.CurrentSchemaVersion)throw new InvalidDataException("Schema ou formato incompatível.");
        var files=m.Bibles??[];
        if(files.Count>MaximumVersions||m.BackupFormatVersion==1&&files.Count!=0)throw new InvalidDataException("Manifesto inválido.");
        var allowed=new HashSet<string>(StringComparer.Ordinal){"manifest.json","bibliatema.db"};var codes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var b in files)
        {
            SafeCode(b.Code);
            if(!codes.Add(b.Code)||b.EntryName!="bibles/"+b.Code+".sqlite"||b.Size<=0||b.Size>MaximumBibleSize||!Regex.IsMatch(b.Sha256,"^[A-Fa-f0-9]{64}$")||b.SchemaVersion<=0||string.IsNullOrWhiteSpace(b.DisplayName))throw new InvalidDataException("Metadados de versão inválidos.");
            allowed.Add(b.EntryName);
        }
        if(allowed.Count!=zip.Entries.Count||zip.Entries.Any(e=>!allowed.Contains(e.FullName)))throw new InvalidDataException("Arquivo ausente ou não declarado no manifesto.");
        CheckActive(files);
        if(m.BackupFormatVersion==2&&(m.DatabaseSize<=0||m.DatabaseSha256 is null))throw new InvalidDataException("Manifesto sem hash do banco.");
        foreach(var e in zip.Entries.Where(e=>e.FullName!="manifest.json"))
        {
            ct.ThrowIfCancellationRequested();var dest=Path.Combine(stage,e.FullName.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await using(var source=e.Open())await using(var output=new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true))await source.CopyToAsync(output,ct);
            var b=files.FirstOrDefault(x=>x.EntryName==e.FullName);var size=b?.Size??m.DatabaseSize;var hash=b?.Sha256??m.DatabaseSha256;
            if(size>0&&new FileInfo(dest).Length!=size)throw new InvalidDataException("Tamanho não confere com o manifesto.");
            if(hash is not null&&!hash.Equals(await Hash(dest,ct),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Checksum não confere com o manifesto.");
            if(b is not null)await ValidateBible(dest,ct);
        }
        if(await ValidateDatabase(Path.Combine(stage,"bibliatema.db"),ct)!=m.SchemaVersion)throw new InvalidDataException("Schema do manifesto não corresponde ao banco.");
        if(m.BackupFormatVersion==2)
        {
            await using var db=await Open(Path.Combine(stage,"bibliatema.db"),true,ct);await using var cmd=db.CreateCommand();cmd.CommandText="SELECT Code,DisplayName,SchemaVersion,Language,IsEnabled,IsBundled,ValidationStatus FROM BibleVersionCatalog WHERE IsInstalled=1;";
            var installed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);await using(var r=await cmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))
            {
                installed.Add(r.GetString(0));var b=files.SingleOrDefault(x=>x.Code.Equals(r.GetString(0),StringComparison.OrdinalIgnoreCase));
                if(b is null||b.DisplayName!=r.GetString(1)||b.SchemaVersion!=r.GetInt32(2)||b.Language!=r.GetString(3)||b.IsEnabled!=(r.GetInt32(4)!=0)||b.IsBundled!=(r.GetInt32(5)!=0)||b.ValidationStatus!=r.GetInt32(6))throw new InvalidDataException("Metadados do catálogo divergem do manifesto.");
            }
            if(!installed.SetEquals(codes))throw new InvalidDataException("Catálogo e manifesto divergem sobre as versões instaladas.");
            cmd.CommandText="SELECT Value FROM Setting WHERE Key='Bible.ActiveVersionCode';";var active=Convert.ToString(await cmd.ExecuteScalarAsync(ct));
            if(files.Count>0&&!string.Equals(files.Single(x=>x.IsActive).Code,active,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Versão ativa diverge do catálogo.");
        }
        return m;
    }
    private async Task Reconcile(string path,Manifest m,string? generation,string rollback,CancellationToken ct)
    {
        await using var db=await Open(path,false,ct);await using var tx=db.BeginTransaction();
        if(generation is not null)
        {
            await using(var clear=db.CreateCommand()){clear.Transaction=tx;clear.CommandText="UPDATE BibleVersionCatalog SET InstalledPath=NULL WHERE IsInstalled=0;";await clear.ExecuteNonQueryAsync(ct);}
            foreach(var b in m.Bibles??[])
            {
                await using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE BibleVersionCatalog SET InstalledPath=$path,DatabaseFileName=$file,IsInstalled=1,IsEnabled=$enabled,IsBundled=$bundled WHERE Code=$code;";
                cmd.Parameters.AddWithValue("$path",Path.Combine(generation,b.Code+".sqlite"));cmd.Parameters.AddWithValue("$file",b.Code+".sqlite");cmd.Parameters.AddWithValue("$enabled",b.IsEnabled?1:0);cmd.Parameters.AddWithValue("$bundled",b.IsBundled?1:0);cmd.Parameters.AddWithValue("$code",b.Code);if(await cmd.ExecuteNonQueryAsync(ct)!=1)throw new InvalidDataException("Versão ausente no catálogo.");
            }
            await using var active=db.CreateCommand();active.Transaction=tx;active.CommandText="DELETE FROM Setting WHERE Key='Bible.ActiveVersionCode';";await active.ExecuteNonQueryAsync(ct);
            var chosen=m.Bibles?.SingleOrDefault(x=>x.IsActive);
            if(chosen is not null){active.CommandText="INSERT INTO Setting(Key,Value,UpdatedAt) VALUES('Bible.ActiveVersionCode',$code,$now);";active.Parameters.AddWithValue("$code",chosen.Code);active.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));await active.ExecuteNonQueryAsync(ct);}
        }
        else
        {
            await using(var attach=db.CreateCommand())
            {
                attach.Transaction=tx;attach.CommandText="ATTACH DATABASE $path AS previous;";attach.Parameters.AddWithValue("$path",rollback);await attach.ExecuteNonQueryAsync(ct);
                attach.CommandText="""
                    INSERT INTO BibleVersionCatalog(Code,DisplayName,Language,DatabaseFileName,InstalledPath,SchemaVersion,LicenseName,LicenseText,Attribution,IsInstalled,IsEnabled,IsBundled,InstalledAt,UpdatedAt,ValidationStatus,ValidationMessage)
                    SELECT Code,DisplayName,Language,DatabaseFileName,InstalledPath,SchemaVersion,LicenseName,LicenseText,Attribution,IsInstalled,IsEnabled,IsBundled,InstalledAt,UpdatedAt,ValidationStatus,ValidationMessage
                    FROM previous.BibleVersionCatalog p WHERE NOT EXISTS(SELECT 1 FROM main.BibleVersionCatalog b WHERE b.Code=p.Code);
                    """;await attach.ExecuteNonQueryAsync(ct);
            }
            await using var old=await Open(rollback,true,ct);var local=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            await using(var cmd=old.CreateCommand()){cmd.CommandText="SELECT Code,InstalledPath FROM BibleVersionCatalog WHERE IsInstalled=1;";await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))if(!r.IsDBNull(1)&&File.Exists(r.GetString(1)))local[r.GetString(0)]=r.GetString(1);}
            var entries=new List<string>();await using(var cmd=db.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="SELECT Code FROM BibleVersionCatalog;";await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))entries.Add(r.GetString(0));}
            foreach(var code in entries)
            {
                var found=local.GetValueOrDefault(code);if(found is not null)await ValidateBible(found,ct);
                await using var cmd=db.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE BibleVersionCatalog SET InstalledPath=$path,IsInstalled=$installed,IsEnabled=CASE WHEN $installed=0 THEN 0 ELSE IsEnabled END WHERE Code=$code;";
                cmd.Parameters.AddWithValue("$path",(object?)found??DBNull.Value);cmd.Parameters.AddWithValue("$installed",found is null?0:1);cmd.Parameters.AddWithValue("$code",code);await cmd.ExecuteNonQueryAsync(ct);
            }
            await using var fix=db.CreateCommand();fix.Transaction=tx;fix.CommandText="UPDATE Setting SET Value=COALESCE((SELECT Code FROM BibleVersionCatalog WHERE IsInstalled=1 AND IsEnabled=1 ORDER BY Code LIMIT 1),'') WHERE Key='Bible.ActiveVersionCode' AND NOT EXISTS(SELECT 1 FROM BibleVersionCatalog WHERE Code=Setting.Value AND IsInstalled=1 AND IsEnabled=1);";await fix.ExecuteNonQueryAsync(ct);
            logger.LogInformation("Backup antigo sem arquivos bíblicos: arquivos locais preservados.");
        }
        await tx.CommitAsync(ct);
    }
    private async Task ValidateQueries(string path,CancellationToken ct)
    {
        await using var db=await Open(path,true,ct);await using var cmd=db.CreateCommand();cmd.CommandText="SELECT InstalledPath FROM BibleVersionCatalog WHERE IsInstalled=1;";await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))await ValidateBible(r.GetString(0),ct);
    }
    private async Task ValidateBible(string path,CancellationToken ct)
    {
        var v=await validator.ValidateAsync(path,ct);if(!v.IsUsable&&!(v.Issues.Count==1&&v.Issues[0]=="Metadata 'name' ausente."))throw new InvalidDataException($"Banco bíblico inválido: {v.Message}");
        await using var db=await Open(path,true,ct);await using var cmd=db.CreateCommand();cmd.CommandText="SELECT v.text FROM verse v JOIN book b ON b.id=v.book_id WHERE b.book_reference_id=1 AND v.chapter=1 AND v.verse=1;";if(string.IsNullOrWhiteSpace(Convert.ToString(await cmd.ExecuteScalarAsync(ct))))throw new InvalidDataException("Consulta bíblica de validação falhou.");
    }
    private static async Task<int> ValidateDatabase(string path,CancellationToken ct)
    {
        await using var db=await Open(path,true,ct);await using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA integrity_check;";if(Convert.ToString(await cmd.ExecuteScalarAsync(ct))!="ok")throw new InvalidDataException("O banco contido no backup está corrompido.");
        cmd.CommandText="PRAGMA foreign_key_check;";await using(var r=await cmd.ExecuteReaderAsync(ct))if(await r.ReadAsync(ct))throw new InvalidDataException("O banco viola a integridade referencial.");
        cmd.CommandText="SELECT COALESCE(MAX(Version),0) FROM SchemaMigration;";var schema=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));if(schema<=0||schema>AppUserDatabase.CurrentSchemaVersion)throw new InvalidDataException("Schema incompatível.");return schema;
    }
    private static void CheckActive(IReadOnlyList<BibleFile> files){if(files.Count>0&&(files.Count(x=>x.IsActive)!=1||files.Any(x=>x.IsActive&&!x.IsEnabled)))throw new InvalidDataException("Defina exatamente uma versão instalada e habilitada como ativa.");}
    private string Stage(){var dir=Path.Combine(paths.CacheDirectory,"backup-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);return dir;}
    private static void SafeCode(string code){if(!Regex.IsMatch(code,"^[A-Za-z0-9][A-Za-z0-9_-]{1,19}$"))throw new InvalidDataException("Código de versão inseguro.");}
    private static async Task<SqliteConnection> Open(string path,bool readOnly,CancellationToken ct){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=readOnly?SqliteOpenMode.ReadOnly:SqliteOpenMode.ReadWriteCreate,Pooling=false}.ToString());try{await c.OpenAsync(ct);return c;}catch{await c.DisposeAsync();throw;}}
    private static async Task Snapshot(string source,string target,CancellationToken ct){await using var a=await Open(source,true,ct);await using var b=await Open(target,false,ct);a.BackupDatabase(b);}
    private static async Task<string> Hash(string path,CancellationToken ct){await using var s=File.OpenRead(path);return Convert.ToHexString(await SHA256.HashDataAsync(s,ct));}
    private static async Task Add(ZipArchive zip,string name,string path,CancellationToken ct){await using var input=File.OpenRead(path);await using var output=zip.CreateEntry(name,CompressionLevel.Optimal).Open();await input.CopyToAsync(output,ct);}
}
