using System;
using System.Linq;
using JacBlack.Infrastructure.Trackers.Nyaa;
using Xunit;

namespace JacBlack.Tests.Nyaa;

/// <summary>
/// Снимок ленты nyaa.si от 29.07.2026 (75 записей, раздел 1_2).
///
/// Источник заведён взамен AnimeTosho, чья лента встала 8 мая. Главное,
/// что здесь проверяется: из ленты собирается magnet без единого запроса
/// наружу — хеш приходит прямо в ней. Если это сломается, трекер станет
/// бесполезным, потому что раздача без magnet никому не нужна.
/// </summary>
public class NyaaParserFixtureTests
{
    static string Xml() => FixtureLoader.Read("Nyaa/feed_anime_en.xml");

    [Fact]
    public void Лента_разбирается_целиком()
    {
        var items = NyaaParser.ParseFeed(Xml());

        // В снимке 75 записей — берём все, ни одна не должна потеряться.
        Assert.True(items.Count >= 70, $"ожидали около 75 записей, получили {items.Count}");
    }

    [Fact]
    public void У_каждой_записи_есть_хеш_название_и_адрес()
    {
        var items = NyaaParser.ParseFeed(Xml());

        Assert.All(items, i =>
        {
            Assert.Equal(40, i.InfoHash.Length);
            Assert.Matches("^[0-9a-f]{40}$", i.InfoHash);
            Assert.False(string.IsNullOrWhiteSpace(i.Title));
            Assert.StartsWith("https://nyaa.si/view/", i.ViewUrl);
        });
    }

    [Fact]
    public void Дата_разбирается_и_свежая()
    {
        var items = NyaaParser.ParseFeed(Xml());

        Assert.All(items, i => Assert.NotEqual(default, i.PubDate));

        // Снимок сделан 29.07.2026 — ленты старше пары лет в нём быть не может.
        Assert.All(items, i => Assert.InRange(i.PubDate, new DateTime(2024, 1, 1), new DateTime(2027, 1, 1)));
    }

    [Fact]
    public void Из_ленты_собирается_магнит_с_трекерами()
    {
        var torrents = NyaaParser.ParseTorrents(NyaaParser.ParseFeed(Xml()));

        Assert.NotEmpty(torrents);
        Assert.All(torrents, t =>
        {
            Assert.StartsWith("magnet:?xt=urn:btih:", t.magnet);

            // Без анонсов ссылка живёт только на DHT — так уже было
            // с kinozal и nnmclub, где 262 тысячи ссылок остались без них.
            Assert.Contains("&tr=", t.magnet);
            Assert.Equal("nyaa", t.trackerName);
            Assert.Equal(new[] { "anime" }, t.types);
        });
    }

    [Fact]
    public void Хеш_из_ленты_попадает_в_магнит_без_изменений()
    {
        var item = NyaaParser.ParseFeed(Xml()).First();
        string magnet = NyaaParser.BuildMagnet(item);

        Assert.Contains(item.InfoHash, magnet);
    }

    [Fact]
    public void Имя_и_адрес_у_записей_заполнены()
    {
        var torrents = NyaaParser.ParseTorrents(NyaaParser.ParseFeed(Xml()));

        Assert.All(torrents, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.name), $"пустое имя у «{t.title}»");
            Assert.False(string.IsNullOrWhiteSpace(t.url));
        });

        Assert.Equal(torrents.Count, torrents.Select(t => t.url).Distinct().Count());
    }

    [Fact]
    public void Мусор_вместо_ленты_не_роняет_разбор()
    {
        Assert.Empty(NyaaParser.ParseFeed(null));
        Assert.Empty(NyaaParser.ParseFeed(""));
        Assert.Empty(NyaaParser.ParseFeed("<html>не лента</html>"));
        Assert.Empty(NyaaParser.ParseFeed("<rss><channel><item><title>без хеша</title></item></channel></rss>"));
        Assert.Null(NyaaParser.MapToTorrentDetails(null));
    }
}
