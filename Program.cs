using System.Diagnostics;
using System.Net;
using Biblia.Application;
using Biblia.Application.Interfaces;
using Biblia.Infrastructure;
using Biblia.Web.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.AddServiceDefaults();
builder.Services.AddApplication().AddWebInfrastructure();
builder.Services.Configure<Biblia.Web.Services.UpdateOptions>(builder.Configuration.GetSection(Biblia.Web.Services.UpdateOptions.SectionName));
builder.Services.AddHttpClient("updates", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<Biblia.Web.Services.IAppUpdateService, Biblia.Web.Services.AppUpdateService>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddOutputCache();

var host = builder.Configuration["LocalHost:Host"] ?? "127.0.0.1";
var port = builder.Configuration.GetValue<int?>("LocalHost:Port") ?? 0;
builder.WebHost.UseUrls($"http://{host}:{port}");
var app = builder.Build();

try
{
    await app.Services.GetRequiredService<IAppInitializationService>().InitializeAsync();
}
catch (Exception exception)
{
    TryWriteStartupLog($"{DateTimeOffset.Now:O} A inicialização dos dados do aplicativo falhou.{Environment.NewLine}{exception}");
    Console.Error.WriteLine($"Não foi possível inicializar a Bíblia Web: {exception.Message}");
    return;
}

var closeServerWhenBrowserCloses = builder.Configuration.GetValue("LocalHost:CloseServerWhenBrowserCloses", true);
var browserSessions = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
var browserSessionGate = new object();
CancellationTokenSource? pendingBrowserShutdown = null;

void CancelPendingBrowserShutdown()
{
    lock (browserSessionGate)
    {
        pendingBrowserShutdown?.Cancel();
        pendingBrowserShutdown?.Dispose();
        pendingBrowserShutdown = null;
    }
}

void ScheduleBrowserShutdownWhenIdle()
{
    if (!closeServerWhenBrowserCloses || !browserSessions.IsEmpty) return;
    CancellationToken token;
    lock (browserSessionGate)
    {
        pendingBrowserShutdown?.Cancel();
        pendingBrowserShutdown?.Dispose();
        pendingBrowserShutdown = new CancellationTokenSource();
        token = pendingBrowserShutdown.Token;
    }
    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            if (browserSessions.IsEmpty && !token.IsCancellationRequested) app.Lifetime.StopApplication();
        }
        catch (OperationCanceledException) { }
    });
}

app.MapPost("/_biblia/browser-open", (HttpContext context) =>
{
    if (!closeServerWhenBrowserCloses || context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)) return Results.NotFound();
    var id = context.Request.Query["id"].ToString();
    if (string.IsNullOrWhiteSpace(id)) return Results.BadRequest();
    browserSessions[id] = 0;
    CancelPendingBrowserShutdown();
    return Results.NoContent();
});
app.MapPost("/_biblia/browser-closed", (HttpContext context) =>
{
    if (!closeServerWhenBrowserCloses || context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)) return Results.NotFound();
    var id = context.Request.Query["id"].ToString();
    if (!string.IsNullOrWhiteSpace(id)) browserSessions.TryRemove(id, out _);
    ScheduleBrowserShutdownWhenIdle();
    return Results.NoContent();
});

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/status/{0}");
app.UseAntiforgery();
app.UseOutputCache();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();
app.MapGet("/download/backup", async (IBackupService backups, CancellationToken ct) =>
{
    var backup = await backups.CreateAsync(ct);
    return Results.File(backup.Path, "application/zip", Path.GetFileName(backup.Path));
});
app.MapGet("/download/backup-file", (string name, IAppPaths paths) =>
{
    if (name != Path.GetFileName(name) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
    var file = Path.Combine(paths.AppDataDirectory, "backups", name);
    return File.Exists(file) ? Results.File(file, "application/zip", name) : Results.NotFound();
});
app.MapGet("/download/theme-report", (string path, IAppPaths paths) =>
{
    var full = Path.GetFullPath(path);
    var allowed = Path.GetFullPath(Path.Combine(paths.CacheDirectory, "exports")) + Path.DirectorySeparatorChar;
    return full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(full).Equals(".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(full)
        ? Results.File(full, "application/pdf", Path.GetFileName(full)) : Results.NotFound();
});

if (builder.Configuration.GetValue("LocalHost:OpenBrowserOnStart", true) && !app.Environment.IsEnvironment("Testing"))
    app.Lifetime.ApplicationStarted.Register(() => OpenBrowserWhenReady(app, closeServerWhenBrowserCloses));

app.Run();

static void OpenBrowserWhenReady(WebApplication app, bool closeServerWhenBrowserCloses)
{
    var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses
        ?.FirstOrDefault(address => address.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
    if (string.IsNullOrWhiteSpace(url))
    {
        app.Logger.LogError("Kestrel iniciou sem endereço HTTP; não foi possível abrir o navegador.");
        return;
    }

    try
    {
        var browser = FindBrowserExecutable(app.Configuration["LocalHost:BrowserExecutable"]);
        Process? process;
        if (browser is not null)
        {
            var profile = Path.Combine(app.Services.GetRequiredService<IAppPaths>().AppDataDirectory, "browser-session");
            Directory.CreateDirectory(profile);
            process = Process.Start(new ProcessStartInfo(browser,
                $"--new-window \"{url}\" --user-data-dir=\"{profile}\" --no-first-run --disable-background-mode")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        else
        {
            process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        if (process is null)
        {
            app.Logger.LogError("O Windows não iniciou um navegador para {Url}.", url);
            Console.Error.WriteLine($"O servidor está ativo em {url}, mas não foi possível abrir o navegador.");
            return;
        }

        app.Logger.LogInformation("Navegador aberto em {Url}.", url);
        if (closeServerWhenBrowserCloses && browser is not null)
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => app.Lifetime.StopApplication();
        }
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Falha ao abrir o navegador em {Url}.", url);
        Console.Error.WriteLine($"O servidor está ativo em {url}, mas o navegador não abriu: {exception.Message}");
    }
}

static string? FindBrowserExecutable(string? configuredBrowserExecutable)
{
    var candidates = new[]
    {
        configuredBrowserExecutable,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe")
    };
    return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
}

static void TryWriteStartupLog(string message)
{
    try
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(root, "BibliaTema", "Logs");
        Directory.CreateDirectory(directory);
        File.AppendAllText(Path.Combine(directory, "startup.log"), message + Environment.NewLine + Environment.NewLine);
    }
    catch { }
}
