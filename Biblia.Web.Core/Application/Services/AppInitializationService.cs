using Biblia.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Biblia.Application.Services;

public sealed class AppInitializationService(IAppDatabase appDatabase,IInitialBibleInstallationService bibleInstaller,ILogger<AppInitializationService> logger):IAppInitializationService
{
    private readonly SemaphoreSlim _gate=new(1,1);
    private bool _initialized;
    public async Task InitializeAsync(CancellationToken cancellationToken=default)
    {
        if(_initialized)return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if(_initialized)return;
            await appDatabase.InitializeAsync(cancellationToken);
            await bibleInstaller.InstallAsync(cancellationToken);
            _initialized=true;
            logger.LogInformation("Inicialização do aplicativo concluída.");
        }
        finally{_gate.Release();}
    }
}
