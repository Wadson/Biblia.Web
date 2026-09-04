using Biblia.Application.Interfaces;using Biblia.Domain.Entities;using Biblia.Infrastructure.Files;using Xunit;
namespace Biblia.Tests.Infrastructure;
public sealed class ThemeVersePdfServiceTests
{
 [Fact]public async Task CreateThemeVersePdf_RendersPortugueseAndPagination(){var root=Environment.GetEnvironmentVariable("BIBLIATEMA_PDF_QA_DIR")??Path.Combine(Path.GetTempPath(),"BibliaTema.PdfTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var now=DateTimeOffset.Now;var theme=new Theme(1,"Esperança","#336699","Consolo",now,now);var reference=new ThemeVerseReportReference(1,"João 3:16","16 Porque Deus amou o mundo de tal maneira.","Observação pastoral.",43,3,16,16);var report=new ThemeVerseReport("Temas e versículos","ACF - Almeida Corrigida e Fiel",now,1,1,[new(theme,[reference])]);var path=await new PdfService(new TestPaths(root)).CreateThemeVersePdfAsync(report,TestContext.Current.CancellationToken);var output=Path.Combine(root,"relatorio-qa.pdf");File.Copy(path,output,true);var bytes=await File.ReadAllBytesAsync(output,TestContext.Current.CancellationToken);Assert.True(bytes.Length>1000);Assert.Equal("%PDF",System.Text.Encoding.ASCII.GetString(bytes,0,4));}
 sealed class TestPaths(string root):IAppPaths{public string AppDataDirectory=>root;public string CacheDirectory=>root;public string GetPrivateFilePath(string fileName)=>Path.Combine(root,fileName);}
}
