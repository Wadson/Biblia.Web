using Biblia.Domain.Entities;

namespace Biblia.Qa;

public static class ThemeReportQaData
{
    public static ThemeVerseReport Create(int themeCount = 50)
    {
        var now = DateTimeOffset.Parse("2026-09-05T23:35:00Z");
        var sections = Enumerable.Range(1, themeCount).Select(t =>
        {
            var count = t == 3 ? 0 : t % 10 == 0 ? 25 : 2;
            var name = $"Tema {t:D2} — Fé, ação & comunhão" + (t % 7 == 0
                ? ": esperança para a família, acolhimento, perseverança e cuidado com o próximo em todos os momentos da vida" : "");
            var items = Enumerable.Range(1, count).Select(i => new ThemeVerseReportReference(t * 100 + i,
                $"João {t}:{i}", $"{i} QA{t:D2}REF{i:D2}. " + string.Join(" ", Enumerable.Repeat(
                    "Texto sintético de QA: o amor inspira a ação, a fé fortalece a esperança e a comunhão promove o cuidado com o próximo.", t % 10 == 0 ? 6 : 1)),
                i == 2 && t % 10 == 0 ? string.Join(" ", Enumerable.Repeat("Observação longa do vínculo: reflexão, gratidão e perseverança.", 85)) :
                i == 1 ? "Observação do vínculo: uma reflexão para a família." : null,
                43, t, i, i, i % 2 == 0 ? "NVI" : "ACF")).ToArray();
            return new ThemeVerseReportSection(new(t, name, t % 2 == 0 ? "#18864B" : "#336699", null, now, now), items);
        }).ToArray();
        return new("Temas e versículos", now, sections.Length, sections.Sum(s => s.References.Count), sections);
    }
}
