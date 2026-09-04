using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using Biblia.Infrastructure.Media;
using SkiaSharp;
using Xunit;

namespace Biblia.Tests.Infrastructure;

public sealed class VerseCardRenderTests
{
    [Theory]
    [InlineData("Vertical 4:5",1080,1350)]
    [InlineData("Story 9:16",1080,1920)]
    [InlineData("Quadrado 1:1",1080,1080)]
    public async Task RenderAsync_CreatesReadablePng(string format,int width,int height)
    {
        var folder=Path.Combine(Path.GetTempPath(),"BibliaTemaTests",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var background=new NaturePhoto(-1,"","","BíbliaTema","","","#0D1E30",true,"#0066CC");
            var service=new SkiaVerseCardService(new Paths(folder),new Media(background));
            var result=await service.RenderAsync(new("Bom dia!","Santificação","Porque esta é a vontade de Deus, a vossa santificação.","1 Tessalonicenses 4:3","NVI",background,.4,format,"Clássico"),TestContext.Current.CancellationToken);
            Assert.True(File.Exists(result.FilePath)); using var bitmap=SKBitmap.Decode(result.FilePath); Assert.Equal(width,bitmap.Width); Assert.Equal(height,bitmap.Height);
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }
    [Fact]
    public async Task RenderAsync_InvalidBackground_UsesGradientFallback()
    {
        var folder=Path.Combine(Path.GetTempPath(),"BibliaTemaTests",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var invalid=Path.Combine(folder,"corrupt.jpg"); await File.WriteAllTextAsync(invalid,"not an image",TestContext.Current.CancellationToken);
            var background=new NaturePhoto(10,"",invalid,"Pexels","","","#0D1E30",false,"#0066CC");
            var service=new SkiaVerseCardService(new Paths(folder),new FileMedia(background,invalid));
            var result=await service.RenderAsync(new("","","A fé vem pelo ouvir.","Romanos 10:17","NVI",background,.4,"Quadrado 1:1","Clássico"),TestContext.Current.CancellationToken);
            using var bitmap=SKBitmap.Decode(result.FilePath);
            Assert.NotNull(bitmap);
            Assert.Equal(1080,bitmap.Width);
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }
    private sealed class Paths(string root):IAppPaths{public string AppDataDirectory=>root;public string CacheDirectory=>root;public string GetPrivateFilePath(string fileName)=>Path.Combine(root,fileName);}
    private sealed class Media(NaturePhoto background):INatureMediaService{public IReadOnlyList<NaturePhoto> GetOfflineBackgrounds()=>[background];public Task<IReadOnlyList<NaturePhoto>> SearchAsync(string query,int page=1,int pageSize=8,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<NaturePhoto>>([]);public Task<string?> GetRenderFileAsync(NaturePhoto photo,CancellationToken cancellationToken=default)=>Task.FromResult<string?>(null);}
    private sealed class FileMedia(NaturePhoto background,string path):INatureMediaService{public IReadOnlyList<NaturePhoto> GetOfflineBackgrounds()=>[background];public Task<IReadOnlyList<NaturePhoto>> SearchAsync(string query,int page=1,int pageSize=8,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<NaturePhoto>>([]);public Task<string?> GetRenderFileAsync(NaturePhoto photo,CancellationToken cancellationToken=default)=>Task.FromResult<string?>(path);}
}
