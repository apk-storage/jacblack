using System.Linq;
using JacBlack.Infrastructure.Trackers.Anidub;
using Xunit;

namespace JacBlack.Tests.Anidub;

/// <summary>
/// Снимок живой страницы tr.anidub.com от 29.07.2026.
///
/// Тесты не проверяют конкретные названия — они меняются каждый день.
/// Проверяется то, что должно оставаться верным при любой переделке
/// разбора: раздачи находятся, у них есть адрес и имя, год в разумных
/// пределах. Это страховка под уход от регулярок к HTML-библиотеке.
/// </summary>
public class AnidubParserFixtureTests
{
    const string Host = "https://tr.anidub.com";

    static string Html() => FixtureLoader.Read("Anidub/list_page1.html");

    [Fact]
    public void Со_страницы_списка_разбираются_раздачи()
    {
        var list = AnidubParser.ParseTorrentListFromHtml(Html(), Host, 1);

        Assert.NotNull(list);
        Assert.True(list.Count >= 10, $"ожидали хотя бы 10 раздач, получили {list.Count}");
    }

    [Fact]
    public void У_каждой_раздачи_есть_адрес_и_имя()
    {
        var list = AnidubParser.ParseTorrentListFromHtml(Html(), Host, 1);

        Assert.All(list, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.url), "пустой адрес");
            Assert.StartsWith("http", t.url);
            Assert.False(string.IsNullOrWhiteSpace(t.title), $"пустой заголовок у {t.url}");
        });
    }

    [Fact]
    public void Адреса_раздач_не_повторяются()
    {
        var list = AnidubParser.ParseTorrentListFromHtml(Html(), Host, 1);
        var unique = list.Select(t => t.url).Distinct().Count();

        Assert.Equal(list.Count, unique);
    }

    [Fact]
    public void Год_выпуска_если_нашёлся_то_правдоподобный()
    {
        var list = AnidubParser.ParseTorrentListFromHtml(Html(), Host, 1);

        foreach (var t in list.Where(i => i.relased > 0))
            Assert.InRange(t.relased, 1950, 2100);
    }

    [Fact]
    public void Пустая_страница_не_роняет_разбор()
    {
        Assert.Empty(AnidubParser.ParseTorrentListFromHtml("", Host, 1));
        Assert.Empty(AnidubParser.ParseTorrentListFromHtml("<html><body>ничего</body></html>", Host, 1));
    }
}
