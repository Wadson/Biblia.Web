using System.Globalization;
using System.Text;
using Biblia.Application.Interfaces;
using Biblia.Domain.Entities;
using SkiaSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Biblia.Infrastructure.Media;

public sealed class SkiaVerseCardService : IVerseCardService
{
    private readonly IAppPaths paths;
    private readonly INatureMediaService media;
    private readonly ILogger<SkiaVerseCardService> logger;

    public SkiaVerseCardService(IAppPaths paths, INatureMediaService media)
        : this(paths, media, NullLogger<SkiaVerseCardService>.Instance) { }

    public SkiaVerseCardService(IAppPaths paths, INatureMediaService media, ILogger<SkiaVerseCardService> logger)
    {
        this.paths = paths;
        this.media = media;
        this.logger = logger;
    }

    public async Task<VerseCardResult> RenderAsync(VerseCardRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("VerseCardStudio: render iniciado. Formato: {Format}; fundo: {BackgroundKind}", request.Format, request.Background.IsLocal ? "local" : "remoto");
        if (request.VerseText.Length > 700) throw new InvalidOperationException("O trecho é longo demais para um card legível. Reduza o intervalo de versículos.");
        var (width, height) = request.Format switch { "Story 9:16" => (1080, 1920), "Quadrado 1:1" => (1080, 1080), _ => (1080, 1350) };
        var backgroundPath = await media.GetRenderFileAsync(request.Background, cancellationToken); cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = new SKBitmap(width, height); using var canvas = new SKCanvas(bitmap); DrawBackground(canvas, width, height, request.Background, backgroundPath);
        using var overlay = new SKPaint { Color = SKColors.Black.WithAlpha((byte)(Math.Clamp(request.OverlayOpacity, .3, .6) * 255)) }; canvas.DrawRect(0, 0, width, height, overlay);
        using var regular = LoadTypeface("Biblia.Fonts.OpenSans-Regular.ttf"); using var semibold = LoadTypeface("Biblia.Fonts.OpenSans-Semibold.ttf");
        var margin = width * .075f; var center = width / 2f; var y = height * (request.Template switch { "Editorial" => .17f, "Minimalista" => .20f, _ => .13f });
        if (!string.IsNullOrWhiteSpace(request.Greeting)) { DrawCentered(canvas, request.Greeting.ToUpperInvariant(), center, y, semibold, request.Template=="Minimalista"?36:45, SKColor.Parse("#F3E5AB")); y += request.Template=="Minimalista"?82:105; }
        if (!string.IsNullOrWhiteSpace(request.Theme)) { DrawCentered(canvas, request.Theme.ToUpperInvariant(), center, y, semibold, request.Template=="Editorial"?38:30, SKColor.Parse("#D4AF37")); y += 92; }
        var quoteSize = request.VerseText.Length switch { < 150 => 58, < 300 => 49, < 480 => 42, _ => 36 }; if(request.Template=="Minimalista")quoteSize-=5; var maxWidth = width - margin * 2;
        var lines = Wrap(request.VerseText, regular, quoteSize, maxWidth); var lineHeight = quoteSize * 1.36f; var textHeight = lines.Count * lineHeight; y = Math.Max(y, (height - textHeight) / 2f - 30);
        foreach (var line in lines) { DrawCentered(canvas, line, center, y, regular, quoteSize, SKColors.White); y += lineHeight; }
        y += 42; DrawCentered(canvas, request.Reference, center, y, semibold, 38, SKColor.Parse("#F3E5AB")); y += 53; DrawCentered(canvas, request.VersionCode, center, y, regular, 25, SKColors.White.WithAlpha(215));
        DrawCentered(canvas, "BíbliaTema", center, height - 105, semibold, 31, SKColors.White); DrawCentered(canvas, request.Background.Attribution, center, height - 54, regular, 19, SKColors.White.WithAlpha(190));
        var folder = Path.Combine(paths.CacheDirectory, "verse-cards", "exports"); Directory.CreateDirectory(folder); var file = Path.Combine(folder, $"bibliatema-{Slug(request.Reference)}.png");
        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("Não foi possível codificar o card em PNG.");
            await using var stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            data.SaveTo(stream);
            await stream.FlushAsync(cancellationToken);
            logger.LogInformation("VerseCardStudio: render concluído e salvo em {FilePath}", file);
            return new(file, width, height, "image/png");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogError(ex, "Falha ao salvar o card em {FilePath}", file);
            throw new InvalidOperationException("Não foi possível salvar o card. Verifique o espaço e as permissões do dispositivo.", ex);
        }
    }
    private static void DrawBackground(SKCanvas canvas, int width, int height, NaturePhoto photo, string? path)
    {
        if (path is not null && File.Exists(path)) { using var image = SKBitmap.Decode(path); if(image is not null&&image.Width>0&&image.Height>0){var scale = Math.Max((float)width / image.Width, (float)height / image.Height); var sourceWidth = width / scale; var sourceHeight = height / scale; var source = new SKRect((image.Width-sourceWidth)/2,(image.Height-sourceHeight)/2,(image.Width+sourceWidth)/2,(image.Height+sourceHeight)/2); canvas.DrawBitmap(image, source, new SKRect(0,0,width,height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)); return;} }
        var first = SKColor.Parse(photo.AverageColor ?? "#0D1E30"); var second = SKColor.Parse(photo.SecondaryColor ?? "#0066CC"); using var paint = new SKPaint { Shader = SKShader.CreateLinearGradient(new(0,0),new(width,height),[first,second],null,SKShaderTileMode.Clamp) }; canvas.DrawRect(0,0,width,height,paint);
    }
    private static SKTypeface LoadTypeface(string resource) { using var stream = typeof(SkiaVerseCardService).Assembly.GetManifestResourceStream(resource); return stream is null ? SKTypeface.Default : SKTypeface.FromStream(stream) ?? SKTypeface.Default; }
    private static void DrawCentered(SKCanvas canvas,string text,float x,float y,SKTypeface typeface,float size,SKColor color){using var font=new SKFont(typeface,size);using var paint=new SKPaint{Color=color,IsAntialias=true};canvas.DrawText(text,x,y,SKTextAlign.Center,font,paint);}
    private static List<string> Wrap(string text,SKTypeface typeface,float size,float maxWidth){using var font=new SKFont(typeface,size);var result=new List<string>();var line=new StringBuilder();foreach(var word in text.Split(' ',StringSplitOptions.RemoveEmptyEntries)){var candidate=line.Length==0?word:$"{line} {word}";if(font.MeasureText(candidate)<=maxWidth)line.Clear().Append(candidate);else{if(line.Length>0)result.Add(line.ToString());line.Clear().Append(word);}}if(line.Length>0)result.Add(line.ToString());return result;}
    private static string Slug(string value){var normalized=value.Normalize(NormalizationForm.FormD);var b=new StringBuilder();foreach(var c in normalized){if(CharUnicodeInfo.GetUnicodeCategory(c)==UnicodeCategory.NonSpacingMark)continue;if(char.IsLetterOrDigit(c))b.Append(char.ToLowerInvariant(c));else if(b.Length>0&&b[^1]!='-')b.Append('-');}return b.ToString().Trim('-');}
}
