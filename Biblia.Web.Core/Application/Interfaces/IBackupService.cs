namespace Biblia.Application.Interfaces;
public interface IBackupService { Task<BackupInfo> CreateAsync(CancellationToken cancellationToken = default); Task<BackupInfo> ValidateAsync(string backupPath, CancellationToken cancellationToken = default); Task RestoreAsync(string backupPath, CancellationToken cancellationToken = default); }
public sealed record BackupInfo(string Path, DateTimeOffset CreatedAt, int SchemaVersion, long SizeBytes);
