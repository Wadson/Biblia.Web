using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Biblia.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Biblia.Web.Services;

public sealed class UpdateOptions
{
    public const string SectionName = "Update";
    public string ManifestUrl { get; init; } = "";
    public bool CheckOnStartup { get; init; } = true;
}

public sealed record UpdateInfo(Version CurrentVersion, Version AvailableVersion, string InstallerUrl, string Description);
public sealed record UpdateCheckResult(UpdateInfo? Update, string? Error = null);

public interface IAppUpdateService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
    Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    void Install(string installerPath);
}

public sealed class AppUpdateService(
    IHttpClientFactory httpClientFactory,
    IOptions<UpdateOptions> options,
    IAppPaths paths,
    IHostApplicationLifetime applicationLifetime,
    ILogger<AppUpdateService> logger) : IAppUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly UpdateOptions _options = options.Value;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ManifestUrl))
            return new(null, "A URL do manifesto de atualização não foi configurada.");

        if (!Uri.TryCreate(_options.ManifestUrl, UriKind.Absolute, out var manifestUri) || manifestUri.Scheme != Uri.UriSchemeHttps)
            return new(null, "A URL do manifesto deve usar HTTPS.");

        try
        {
            using var response = await httpClientFactory.CreateClient("updates").GetAsync(manifestUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions, cancellationToken);
            if (manifest is null || !Version.TryParse(manifest.Version, out var available) || string.IsNullOrWhiteSpace(manifest.Url))
                return new(null, "O version.json publicado é inválido.");

            if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var installerUri) || installerUri.Scheme != Uri.UriSchemeHttps ||
                !installerUri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return new(null, "A URL do instalador no version.json deve ser HTTPS e terminar em .exe.");

            var current = GetCurrentVersion();
            return available > current
                ? new(new UpdateInfo(current, available, installerUri.ToString(), manifest.Description?.Trim() ?? "Nova versão disponível."))
                : new(null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Não foi possível consultar atualizações.");
            return new(null, "Não foi possível verificar atualizações agora.");
        }
    }

    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(update.InstallerUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O endereço do instalador é inválido.");

        var updateDirectory = Path.Combine(paths.CacheDirectory, "updates");
        Directory.CreateDirectory(updateDirectory);
        var destination = Path.Combine(updateDirectory, $"BibliaTema-{update.AvailableVersion}.exe");
        var temporary = destination + ".download";

        try
        {
            using var response = await httpClientFactory.CreateClient("updates").GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                readTotal += read;
                if (length is > 0) progress?.Report((double)readTotal / length.Value);
            }
            await target.FlushAsync(cancellationToken);
            File.Move(temporary, destination, true);
            progress?.Report(1);
            return destination;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    public void Install(string installerPath)
    {
        if (!File.Exists(installerPath) || !installerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O instalador baixado não foi encontrado.");

        Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
        applicationLifetime.StopApplication();
    }

    private static Version GetCurrentVersion()
    {
        var text = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        text = text?.Split('+', 2)[0];
        return Version.TryParse(text, out var version) ? version : new Version(1, 0, 0);
    }

    private sealed record UpdateManifest(
        [property: JsonPropertyName("versao")] string? Version,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("descricao")] string? Description);
}
