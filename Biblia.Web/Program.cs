using System.Diagnostics;
using Biblia.Application;
using Biblia.Application.Interfaces;
using Biblia.Infrastructure;
using Biblia.Web.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.Services.AddApplication().AddWebInfrastructure();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.WebHost.UseUrls($"http://{builder.Configuration["LocalHost:Host"] ?? "127.0.0.1"}:{builder.Configuration.GetValue<int?>("LocalHost:Port") ?? 0}");

var app = builder.Build();
await app.Services.GetRequiredService<IAppInitializationService>().InitializeAsync();

app.Use(async (context, next) =>
{
    var correlationId = context.TraceIdentifier;
    context.Response.Headers["X-Correlation-Id"] = correlationId;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
}
app.UseStatusCodePagesWithReExecute("/status/{0}");

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.MapGet("/download/backup", async (IBackupService backups, CancellationToken ct) =>
{
    var backup = await backups.CreateAsync(ct);
    return Results.File(backup.Path, "application/zip", Path.GetFileName(backup.Path));
});
app.MapGet("/download/backup-file", (string name,IAppPaths paths)=>
{
    if(name!=Path.GetFileName(name)||!name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))return Results.NotFound();
    var file=Path.Combine(paths.AppDataDirectory,"backups",name);
    return File.Exists(file)?Results.File(file,"application/zip",name):Results.NotFound();
});
app.MapGet("/download/theme-report", (string path, IAppPaths paths) =>
{
    var full = Path.GetFullPath(path);
    var allowed = Path.GetFullPath(Path.Combine(paths.CacheDirectory, "exports")) + Path.DirectorySeparatorChar;
    return full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(full).Equals(".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(full)
        ? Results.File(full, "application/pdf", Path.GetFileName(full)) : Results.NotFound();
});
var openBrowser = builder.Configuration.GetValue("LocalHost:OpenBrowserOnStart", true)
    && !app.Environment.IsEnvironment("Testing");
var closeServerWhenBrowserCloses = builder.Configuration.GetValue("LocalHost:CloseServerWhenBrowserCloses", true);
var configuredBrowserExecutable = builder.Configuration["LocalHost:BrowserExecutable"];
if (openBrowser)
{
    var opened = 0;
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        if (Interlocked.Exchange(ref opened, 1) != 0)
            return;

        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        var url = addresses?.FirstOrDefault(address => address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? app.Urls.FirstOrDefault(address => address.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(url))
            return;

        var browserProcess = StartManagedBrowser(
            url,
            configuredBrowserExecutable,
            app.Services.GetRequiredService<IAppPaths>().AppDataDirectory,
            app.Logger);
        if (browserProcess is null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                app.Logger.LogWarning("O navegador padrão foi aberto, mas não pode ser acompanhado. Configure LocalHost:BrowserExecutable para encerrar o Kestrel ao fechar a janela.");
            }
            catch (Exception exception)
            {
                app.Logger.LogWarning(exception, "O Kestrel iniciou em {Url}, mas o navegador não pôde ser aberto automaticamente.", url);
            }
        }
        else if (closeServerWhenBrowserCloses)
        {
            browserProcess.EnableRaisingEvents = true;
            browserProcess.Exited += (_, _) => app.Lifetime.StopApplication();
            if (browserProcess.HasExited)
                app.Lifetime.StopApplication();
        }
    });
}

app.Run();

static Process? StartManagedBrowser(string url, string? configuredBrowserExecutable, string appDataDirectory, ILogger logger)
{
    var browserExecutable = FindBrowserExecutable(configuredBrowserExecutable);
    if (browserExecutable is null)
    {
        logger.LogWarning("Nenhum Edge ou Chrome foi encontrado para acompanhar a janela do navegador.");
        return null;
    }

    var profileDirectory = Path.Combine(appDataDirectory, "browser-session");
    Directory.CreateDirectory(profileDirectory);
    var arguments = $"--new-window \"{url}\" --user-data-dir=\"{profileDirectory}\" --no-first-run --disable-background-mode";
    return Process.Start(new ProcessStartInfo(browserExecutable, arguments)
    {
        UseShellExecute = false,
        CreateNoWindow = true
    });
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
