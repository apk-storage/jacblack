using System;
using System.Linq;
using JacBlack.Infrastructure.Trackers.Kinozal;
using JacBlack.Models.Details;
using Xunit;

namespace JacBlack.Tests.Kinozal;

/// <summary>
/// Свежее из rss.xml вместо добора по номерам. Фикстура — пять позиций
/// настоящей ленты 04.11.2026, одна из них музыка.
/// </summary>
public class KinozalRssTests
{
    public KinozalRssTests()
    {
        _ = AppInit.conf.Kinozal.host;
    }

    [Fact]
    public void ParseRss_OurCategoriesOnly()
    {
        var list = KinozalParser.ParseRss(FixtureLoader.Read("Kinozal/rss.xml"));

        Assert.Equal(new[] { "2159736", "2159751", "2159648", "2159694" },
            list.Select(t => t.url.Substring(t.url.LastIndexOf('=') + 1)).ToArray());
        Assert.DoesNotContain(list, t => t.title.Contains("MP3"));
    }

    [Fact]
    public void ParseRss_Fields()
    {
        var t = KinozalParser.ParseRss(FixtureLoader.Read("Kinozal/rss.xml"))[0];

        // Тот же ключ, что у списка: иначе одна раздача ляжет дважды.
        Assert.Equal($"{AppInit.conf.Kinozal.host}/details.php?id=2159736", t.url);
        Assert.Equal("Дочь (Отверженные) / 2026 / РУ / WEB-DL (1080p)", t.title);
        Assert.Equal(new[] { "movie" }, t.types);
        Assert.Equal(2026, t.relased);
        // pubDate «17:53:28 +0300» — московское время с меткой Z, как у списка.
        Assert.Equal(new DateTime(2026, 11, 4, 17, 53, 28, DateTimeKind.Utc), t.createTime);
        Assert.Equal("", t.sizeName);
        Assert.Equal(0, t.sid);
    }

    [Fact]
    public void ParseRss_Serial()
    {
        var t = KinozalParser.ParseRss(FixtureLoader.Read("Kinozal/rss.xml"))
            .Single(x => x.url.EndsWith("=2159648"));
        Assert.Equal(new[] { "multfilm", "multserial" }, t.types);
        Assert.Equal("Сестры Гримм", t.name);
    }

    [Fact]
    public void ParseRss_Garbage_Empty()
    {
        Assert.Empty(KinozalParser.ParseRss(null));
        Assert.Empty(KinozalParser.ParseRss("<html>Увы, вы Зритель. Вам доступно к просмотру всего 100 раздач в сутки.</html>"));
    }

    [Fact]
    public void ShouldSkipHashFetch_RecordFromFeed_NoSecondRequest()
    {
        // Лента обрезает название, список потом принесёт полное и размер:
        // хеш у записи уже свежий, второй запрос съел бы суточный лимит.
        var fromFeed = new TorrentDetails { magnet = "magnet:?xt=urn:btih:A", title = "Кровавый кулак 5 ...", sizeName = "", createTime = new DateTime(2026, 11, 4, 16, 20, 0) };
        var fromList = new TorrentDetails { title = "Кровавый кулак 5: Живая мишень / Bloodfist V / 1993", sizeName = "1.46 ГБ", createTime = new DateTime(2026, 11, 4, 14, 2, 0) };
        Assert.True(KinozalParser.ShouldSkipHashFetch(fromFeed, fromList));
    }

    [Fact]
    public void ShouldSkipHashFetch_ListRecordChanged_Asks()
    {
        var cached = new TorrentDetails { magnet = "magnet:?xt=urn:btih:A", title = "Сериал (1-3 серии)", sizeName = "3 ГБ", createTime = new DateTime(2026, 11, 3) };
        Assert.False(KinozalParser.ShouldSkipHashFetch(cached, new TorrentDetails { title = "Сериал (1-4 серии)", sizeName = "3 ГБ", createTime = new DateTime(2026, 11, 3) }));
        Assert.False(KinozalParser.ShouldSkipHashFetch(cached, new TorrentDetails { title = "Сериал (1-3 серии)", sizeName = "4 ГБ", createTime = new DateTime(2026, 11, 3) }));
        Assert.True(KinozalParser.ShouldSkipHashFetch(cached, new TorrentDetails { title = "Сериал (1-3 серии)", sizeName = "3 ГБ", createTime = new DateTime(2026, 11, 3) }));
        Assert.False(KinozalParser.ShouldSkipHashFetch(new TorrentDetails { title = "x", sizeName = "" }, new TorrentDetails { title = "x" }));
    }
}
