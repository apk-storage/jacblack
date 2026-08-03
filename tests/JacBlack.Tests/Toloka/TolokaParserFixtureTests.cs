using System;
using System.Linq;
using JacBlack.Infrastructure.Trackers.Toloka;
using Xunit;

namespace JacBlack.Tests.Toloka;

/// <summary>
/// Снимок раздела f16 украинской Toloka от 29.07.2026 (страница под учёткой,
/// имя пользователя из снимка вычищено).
///
/// Toloka закрывает то, ради чего когда-то думали про Mazepa: украинское
/// озвучение. Разбор идёт по строкам таблицы, и первым отваливается разбор
/// даты — именно он отсеивает строки, поэтому его и проверяем в первую очередь.
/// </summary>
public class TolokaParserFixtureTests
{
    static string Html() => FixtureLoader.Read("Toloka/f16.html");

    [Fact]
    public void Со_страницы_раздела_разбираются_раздачи()
    {
        var list = TolokaParser.ParseTorrentsFromPage(Html(), "16");

        Assert.NotNull(list);
        Assert.True(list.Count >= 5, $"ожидали хотя бы 5 раздач, получили {list.Count}");
    }

    [Fact]
    public void У_каждой_раздачи_есть_адрес_заголовок_и_дата()
    {
        var list = TolokaParser.ParseTorrentsFromPage(Html(), "16");

        Assert.All(list, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.url), "пустой адрес");
            Assert.False(string.IsNullOrWhiteSpace(t.title), $"пустой заголовок у {t.url}");

            // Строка без разобранной даты вообще не должна попадать в выдачу:
            // на ней разбор и отсеивает служебные строки таблицы.
            Assert.NotEqual(default, t.createTime);
            Assert.InRange(t.createTime, new DateTime(2000, 1, 1), DateTime.UtcNow.AddDays(2));
        });
    }

    [Fact]
    public void Адреса_не_повторяются()
    {
        var list = TolokaParser.ParseTorrentsFromPage(Html(), "16");

        Assert.Equal(list.Count, list.Select(t => t.url).Distinct().Count());
    }

    [Fact]
    public void Тип_раздачи_проставлен()
    {
        var list = TolokaParser.ParseTorrentsFromPage(Html(), "16");

        Assert.All(list, t =>
        {
            Assert.NotNull(t.types);
            Assert.NotEmpty(t.types);
        });
    }

    [Fact]
    public void Строки_сбора_средств_пропускаются()
    {
        // На Toloka в таблице попадаются строки «Збір коштів» — это не раздачи.
        var list = TolokaParser.ParseTorrentsFromPage(Html(), "16");

        Assert.All(list, t => Assert.DoesNotContain("збір коштів", (t.title ?? "").ToLowerInvariant()));
    }

    [Fact]
    public void Пустая_страница_не_роняет_разбор()
    {
        Assert.Empty(TolokaParser.ParseTorrentsFromPage("", "16"));
        Assert.Empty(TolokaParser.ParseTorrentsFromPage("<html><body>пусто</body></html>", "16"));
    }
}
