using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using Biblia.Infrastructure.AppDatabase;
using Biblia.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable xUnit1051

namespace Biblia.Tests.Integration;

public sealed class PublicationHeaderSettingsTests
{
    [Fact]
    public async Task SavesIndependentHeaderColorsAndBrandingLogo()
    {
        var folder=Path.Combine(Path.GetTempPath(),"BibliaTema.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var database=new AppDatabase(Path.Combine(folder,"app.db"),NullLogger<AppDatabase>.Instance);
            var service=new PublicationService(database,new Clock());
            await service.SaveBrandingAsync("Igreja Teste",[1,2,3],"image/png");
            var publication=new Publication(0,"Publicação", "Título", "Subtítulo", "Detalhe", "#123456", "#FFFFFF", 22, true, false, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            { TitleTextColorHex="#AA0000",OrganizationTextColorHex="#00AA00",OrganizationFontSize=12,SubtitleTextColorHex="#0000AA",SubtitleFontSize=10,HeaderDetailTextColorHex="#AA00AA",HeaderDetailFontSize=9 };
            var saved=await service.SaveAsync(publication);
            var loaded=await service.GetAsync(saved.Id);
            Assert.Equal("#AA0000",loaded!.TitleTextColorHex);Assert.Equal("#00AA00",loaded.OrganizationTextColorHex);Assert.Equal("#0000AA",loaded.SubtitleTextColorHex);Assert.Equal("#AA00AA",loaded.HeaderDetailTextColorHex);Assert.Equal("#123456",loaded.HeaderBackgroundColorHex);
            var branding=await service.GetBrandingAsync();Assert.Equal("Igreja Teste",branding!.Name);Assert.Equal([1,2,3],branding.Logo);
        }
        finally{SqliteConnection.ClearAllPools();if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}
#pragma warning restore xUnit1051
