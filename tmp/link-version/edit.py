from pathlib import Path
def edit(path, fn):
 p=Path(path); p.write_text(fn(p.read_text(encoding='utf-8')),encoding='utf-8')
repo='Biblia.Web.Core/Infrastructure/Repositories/SavedReferenceRepository.cs'
def repository(s):
 s=s.replace('INSERT INTO ReferenceTheme(ReferenceId,ThemeId) VALUES($reference,$theme);','INSERT OR IGNORE INTO ReferenceTheme(ReferenceId,ThemeId,BibleVersionId) SELECT $reference,$theme,PreferredBibleVersionId FROM SavedReference WHERE Id=$reference;')
 s=s.replace('delete.CommandText = "DELETE FROM ReferenceTheme WHERE ReferenceId=$reference;";', '''var retained = themeIds.Distinct().ToArray();
            var parameters = retained.Select((_, i) => "$keep" + i).ToArray();
            delete.CommandText = "DELETE FROM ReferenceTheme WHERE ReferenceId=$reference" +
                (retained.Length == 0 ? ";" : $" AND ThemeId NOT IN ({string.Join(',', parameters)});");
            for (var i = 0; i < retained.Length; i++) delete.Parameters.AddWithValue(parameters[i], retained[i]);''')
 s=s.replace('ReferenceTheme(ReferenceId,ThemeId,Observation,CreatedAt,UpdatedAt) VALUES($reference,$theme,$observation,$now,$now)', 'ReferenceTheme(ReferenceId,ThemeId,Observation,CreatedAt,UpdatedAt,BibleVersionId) VALUES($reference,$theme,$observation,$now,$now,$version)')
 s=s.replace('link.Parameters.AddWithValue("$reference", referenceId);', 'link.Parameters.AddWithValue("$version", preferredVersionId);\n                link.Parameters.AddWithValue("$reference", referenceId);')
 s=s.replace('rt.Observation,rt.CreatedAt,rt.UpdatedAt\n','rt.Observation,rt.CreatedAt,rt.UpdatedAt,rt.BibleVersionId\n')
 s=s.replace('reader.GetString(2), created, updated));','reader.GetString(2), created, updated, reader.IsDBNull(5) ? null : reader.GetInt64(5)));')
 return s
edit(repo,repository)
def ui(s):
 s=s.replace('@inject Biblia.Application.Interfaces.IBibleVersionManager Versions\n','')
 start=s.index('<label>Versão bíblica'); end=s.index('</label>',start)+len('</label>'); s=s[:start]+s[end:]
 s=s[:s.index('@code{')]+'''@code {
    ReportsOverview? stats;
    IReadOnlyList<Theme> themes = [];
    long themeId;
    string? pdfPath, error, note;
    string Summary => $"Tema: {(themeId == 0 ? "Todos" : themes.FirstOrDefault(x => x.Id == themeId)?.Name)}";
    protected override async Task OnInitializedAsync()
    {
        stats = await ReportService.GetOverviewAsync();
        themes = await Themes.SearchAsync(null);
    }
    async Task Generate()
    {
        try
        {
            error = null;
            pdfPath = null;
            note = null;
            var report = await ReportService.BuildThemesAsync(new(themeId == 0 ? null : themeId));
            pdfPath = await Pdf.CreateThemeVersePdfAsync(report);
            note = $"Relatório gerado com {report.ThemeCount} tema(s) e {report.ReferenceCount} referência(s).";
        }
        catch (Exception ex) { error = ex.Message; }
    }
}
'''
 return s
edit('Biblia.Web.Web/Components/Pages/Reports.razor',ui)
def pdf(s):
 s=s.replace('new[] { "VERSÃO DA BÍBLIA", "GERADO EM", "RESUMO DO FILTRO" }','new[] { "GERADO EM", "RESUMO DO FILTRO" }')
 s=s.replace('new[] { report.VersionLabel,\n            report.GeneratedAt','new[] { report.GeneratedAt')
 s=s.replace('(Width - 36) / 3','(Width - 36) / 2')
 s=s.replace('Wrap(item.FormattedReference, reference, Width - 2 * Padding)','Wrap(CardTitle(item), reference, Width - 2 * Padding)')
 s=s.replace('    private double CardHeight(','''    private static string CardTitle(ThemeVerseReportReference item) =>
        string.IsNullOrWhiteSpace(item.VersionCode) ? item.FormattedReference : $"{item.FormattedReference}  [{item.VersionCode}]";

    private double CardHeight(''')
 return s
edit('Biblia.Web.Core/Infrastructure/Files/PdfService.cs',pdf)
for p in [Path('tools/ThemeReportQa/ThemeReportQaData.cs'),Path('Biblia.Web.Tests/Infrastructure/ThemeVersePdfServiceTests.cs')]:
 edit(p,lambda s:s.replace('"Temas e versículos", "ACF - Almeida Corrigida e Fiel",','"Temas e versículos",'))
edit('Biblia.Web.Tests/Infrastructure/ThemeVersePdfServiceTests.cs',lambda s:s.replace('observation, 43, 3, id, id);','observation, 43, 3, id, id, "ACF");').replace('Assert.Contains("ACF - Almeida Corrigida e Fiel", text);','Assert.Contains("[ACF]", text);\n        Assert.DoesNotContain("VERSÃO DA BÍBLIA", text);'))
for path in ['Biblia.Web.Tests/Application/ThemeReportServiceTests.cs','Biblia.Web.Tests/Application/CanonicalThemeFlowTests.cs']:
 edit(path,lambda s:s.replace('new(1,"ACF")','new(1)').replace('new(null, "NVI")','new(null)').replace('new(2, "NVI")','new(2)').replace('new(null, "ACF")','new(null)').replace('Assert.Equal("NVI - Nova Versão Internacional", report.VersionLabel);','Assert.All(section.References, r => Assert.Equal("NVI", r.VersionCode));'))
