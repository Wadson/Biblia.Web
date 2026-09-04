namespace Biblia.Application.Interfaces;

public interface IFileTransferService
{
    Task<FileSaveOutcome> SaveCopyAsync(string path,CancellationToken cancellationToken=default);
    Task ShareAsync(string path,CancellationToken cancellationToken=default);
    Task<string?> PickBackupAsync(CancellationToken cancellationToken=default);
}

public enum FileSaveOutcome { Saved, Cancelled }

public interface IAppInfoService
{
    string Version{get;}
    string Build{get;}
}
