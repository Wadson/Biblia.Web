using Biblia.Application;
using Biblia.Application.Interfaces;
using Biblia.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// Only run against a separately created audit snapshot, never the active database.
if (args.Length != 1) throw new ArgumentException("Informe a pasta da cópia de auditoria.");
var root = Path.GetFullPath(args[0]);
var live = new LocalAppPaths().AppDataDirectory;
Console.WriteLine($"Default app data: {live}");
if (root.Equals(live, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(root, "bibliatema.db")))
    throw new ArgumentException("É obrigatória uma cópia separada do banco.");
var services = new ServiceCollection();
services.AddLogging();
services.AddApplication().AddWebInfrastructure();
services.AddSingleton<IAppPaths>(new AuditPaths(root));
await using var provider = services.BuildServiceProvider();
Console.WriteLine($"Assembly: {typeof(LocalAppPaths).Assembly.Location}");
try
{
    var reports = provider.GetRequiredService<IReportService>();
    var overview = await reports.GetOverviewAsync();
    Console.WriteLine($"Overview: {overview}");
    var report = await reports.BuildThemesAsync(new(null));
    Console.WriteLine($"BuildThemes OK: {report.ThemeCount} temas; {report.ReferenceCount} vínculos");
    var pdf = await provider.GetRequiredService<IPdfService>().CreateThemeVersePdfAsync(report);
    Console.WriteLine($"PDF OK: {pdf}");
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    Environment.ExitCode = 1;
}

sealed class AuditPaths(string root) : IAppPaths
{
    public string AppDataDirectory => root;
    public string CacheDirectory => Path.Combine(root, "Cache");
    public string GetPrivateFilePath(string name) => Path.Combine(root, name);
}
