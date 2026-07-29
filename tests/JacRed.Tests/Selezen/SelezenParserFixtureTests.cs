using System.Linq;
using JacRed.Infrastructure.Trackers.Selezen;
using Xunit;

namespace JacRed.Tests.Selezen;

/// <summary>
/// Снимок страницы selezen.net от 29.07.2026.
///
/// У Selezen magnet лежит не в списке, а на странице раздачи, поэтому
/// проверяются две вещи по отдельности: разбор списка и вытаскивание
/// magnet из страницы раздачи.
/// </summary>
public class SelezenParserFixtureTests
{
    static string Html() => FixtureLoader.Read("Selezen/list_page1.html");

    [Fact]
    public void Со_страницы_списка_разбираются_раздачи()
    {
        var list = SelezenParser.ParseTorrentsFromListPage(Html());

        Assert.NotNull(list);
        Assert.True(list.Count >= 5, $"ожидали хотя бы 5 раздач, получили {list.Count}");
    }

    [Fact]
    public void У_каждой_раздачи_есть_адрес_и_заголовок()
    {
        var list = SelezenParser.ParseTorrentsFromListPage(Html());

        Assert.All(list, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.url), "пустой адрес");
            Assert.False(string.IsNullOrWhiteSpace(t.title), $"пустой заголовок у {t.url}");
        });
    }

    [Fact]
    public void Адреса_не_повторяются()
    {
        var list = SelezenParser.ParseTorrentsFromListPage(Html());

        Assert.Equal(list.Count, list.Select(t => t.url).Distinct().Count());
    }

    [Fact]
    public void Аниме_из_списка_исключается()
    {
        // Разбор нарочно пропускает строки с меткой «Аниме»: этот раздел
        // закрывают специализированные трекеры, а здесь он дал бы дубли.
        var list = SelezenParser.ParseTorrentsFromListPage(Html());

        Assert.All(list, t => Assert.DoesNotContain("аниме", (t.title ?? "").ToLowerInvariant()));
    }

    [Theory]
    [InlineData("<a href=\"magnet:?xt=urn:btih:c246c69fdf3b362eeda847166ec45093648e6ba8&dn=Test\">качать</a>",
                "magnet:?xt=urn:btih:c246c69fdf3b362eeda847166ec45093648e6ba8")]
    public void Из_страницы_раздачи_достаётся_магнит(string html, string expectedPrefix)
    {
        string magnet = SelezenParser.ExtractMagnetFromDetailPage(html);

        Assert.NotNull(magnet);
        Assert.StartsWith(expectedPrefix, magnet);
    }

    [Fact]
    public void Страница_без_магнита_даёт_пустоту_а_не_ошибку()
    {
        Assert.True(string.IsNullOrEmpty(SelezenParser.ExtractMagnetFromDetailPage("<html>нет ссылки</html>")));
        Assert.True(string.IsNullOrEmpty(SelezenParser.ExtractMagnetFromDetailPage("")));
    }

    [Fact]
    public void Пустая_страница_списка_не_роняет_разбор()
    {
        Assert.Empty(SelezenParser.ParseTorrentsFromListPage(""));
        Assert.Empty(SelezenParser.ParseTorrentsFromListPage("<html><body>пусто</body></html>"));
    }
}
