namespace Biblia.Application.Interfaces;

public interface IAppPaths
{
    string AppDataDirectory { get; }
    string CacheDirectory { get; }
    string GetPrivateFilePath(string fileName);
}
