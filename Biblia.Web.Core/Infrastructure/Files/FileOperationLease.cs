namespace Biblia.Infrastructure.Files;

// Exclusive OS file handle coordinates imports, backups and restores across processes.
public static class FileOperationLease
{
    public static async Task<IDisposable> AcquireAsync(string root,CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var path=Path.Combine(root,".versions-maintenance.lock");
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
            catch(IOException) { await Task.Delay(100,ct); }
        }
    }
}
