using System.Diagnostics;
using Biblia.Application;
using Biblia.Application.Interfaces;
using Biblia.Infrastructure;
using Biblia.Web.Web.Components;

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
app.MapGet("/download/theme-report", (string path, IAppPaths paths) =>
{
    var full = Path.GetFullPath(path);
    var allowed = Path.GetFullPath(Path.Combine(paths.CacheDirectory, "exports")) + Path.DirectorySeparatorChar;
    return full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(full).Equals(".pdf", StringComparison.OrdinalIgnoreCase) && File.Exists(full)
        ? Results.File(full, "application/pdf", Path.GetFileName(full)) : Results.NotFound();
});
app.MapGet("/download/card", (string path, IAppPaths paths) =>
{
    var full = Path.GetFullPath(path);
    var allowed = Path.GetFullPath(Path.Combine(paths.CacheDirectory, "verse-cards", "exports")) + Path.DirectorySeparatorChar;
    return full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && File.Exists(full)
        ? Results.File(full, "image/png", Path.GetFileName(full))
        : Results.NotFound();
});

var openBrowser = builder.Configuration.GetValue("LocalHost:OpenBrowserOnStart", true)
    && !app.Environment.IsEnvironment("Testing") && !Console.IsInputRedirected;
if (openBrowser)
{
    var opened = 0;
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        if (Interlocked.Exchange(ref opened, 1) != 0) return;
        var url = app.Urls.FirstOrDefault(x => x.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (url is not null) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    });
}

app.Run();
