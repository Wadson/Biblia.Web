namespace Biblia.Infrastructure.Files;

// Exclusive OS file handle coordinates imports, backups and restores across processes.
public static class FileOperationLease
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static async Task<IDisposable> AcquireAsync(string root,CancellationToken ct, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(root);
        var path=Path.Combine(root,".versions-maintenance.lock");
        using var timeoutSource=new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var waitSource=CancellationTokenSource.CreateLinkedTokenSource(ct,timeoutSource.Token);
        while(true)
        {
            if (!ct.IsCancellationRequested && timeoutSource.IsCancellationRequested)
                throw new TimeoutException("A operação não pôde começar porque outra manutenção de dados ainda está em andamento.");
            waitSource.Token.ThrowIfCancellationRequested();
            try { return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
            catch(IOException)
            {
                try { await Task.Delay(100,waitSource.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutSource.IsCancellationRequested)
                {
                    throw new TimeoutException("A operação não pôde começar porque outra manutenção de dados ainda está em andamento.");
                }
            }
        }
    }
}
