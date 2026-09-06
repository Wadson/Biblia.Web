using Biblia.Application.Interfaces;
using Biblia.Infrastructure.Files;
using Biblia.Qa;
using Biblia.Application.Services;
using Biblia.Infrastructure.AppDatabase;
using Biblia.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

if (args.Length > 1 && args[0] == "audit-canonical")
{
    var validator = new Biblia.Infrastructure.BibleDatabases.BibleValidationService(NullLogger<Biblia.Infrastructure.BibleDatabases.BibleValidationService>.Instance);
    var audit = new List<object>();
    var invalid = false;
    foreach (var directory in args.Skip(1))
        foreach (var file in Directory.EnumerateFiles(directory, "*.sqlite", SearchOption.AllDirectories).Order())
        {
            var issues = await validator.ValidateCanonicalBooksAsync(file);
            invalid |= issues.Count != 0;
            audit.Add(new { Path = Path.GetFullPath(file), Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(file))), Issues = issues });
        }
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(audit, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Environment.ExitCode = invalid ? 1 : 0;
    return;
}

if (args.Length == 2 && args[1].StartsWith("typography"))
{
    var root = Path.GetFullPath(args[0]);
    Directory.CreateDirectory(root);
    var settings = new SettingsService(new SettingsRepository(new AppDatabase(Path.Combine(root, "qa-settings.db"), NullLogger<AppDatabase>.Instance), new QaClock()));
    var data = ThemeReportQaData.Create(26);
    // Include a wrapped reference without changing the standard TOC fixture.
    data = data with { Sections = data.Sections.Select((s, i) => i == 0 ? s with { References = s.References.Select((r, j) => j == 0 ? r with { FormattedReference = "Segunda Epístola de Paulo aos Tessalonicenses 3:1-18 (referência longa de QA)" } : r).ToArray() } : s).ToArray() };
    var service = new PdfService(new QaPaths(root), settings);
    if (args[1] == "typography-read")
    {
        if (await settings.GetPdfTypographyAsync() != new PdfReportTypographyOptions(24, 22, 20)) throw new Exception("Persistência entre processos falhou.");
        var generated = await service.CreateThemeVersePdfAsync(data);
        File.Copy(generated, Path.Combine(root, "qa-reabertura.pdf"), true);
        Console.WriteLine("Persistência entre processos e geração publicada: OK");
    }
    else
    {
        foreach (var (name, options) in new[] { ("padrao", PdfReportTypographyOptions.Default), ("minimo", new PdfReportTypographyOptions(8, 8, 7)), ("maximo", new PdfReportTypographyOptions(24, 22, 20)) })
        {
            await settings.SavePdfTypographyAsync(options);
            var generated = await service.CreateThemeVersePdfAsync(data);
            var target = Path.Combine(root, $"fontes-{name}.pdf");
            File.Copy(generated, target, true);
            Console.WriteLine(target);
        }
    }
    Console.WriteLine(typeof(PdfService).Assembly.Location);
    Console.WriteLine($"{data.ThemeCount} temas; {data.ReferenceCount} referências");
    return;
}
if (args.Length != 1) throw new ArgumentException("Informe a pasta de saída do PDF de QA.");
var report = ThemeReportQaData.Create();
var path = await new PdfService(new QaPaths(Path.GetFullPath(args[0]))).CreateThemeVersePdfAsync(report);
Console.WriteLine(typeof(PdfService).Assembly.Location);
Console.WriteLine($"{report.ThemeCount} temas; {report.ReferenceCount} referências");
Console.WriteLine(path);

sealed class QaPaths(string root) : IAppPaths
{
    public string AppDataDirectory => root;
    public string CacheDirectory => root;
    public string GetPrivateFilePath(string name) => Path.Combine(root, name);
}

sealed class QaClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
