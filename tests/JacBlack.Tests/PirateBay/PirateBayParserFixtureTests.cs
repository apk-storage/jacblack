using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Trackers.PirateBay;
using Newtonsoft.Json;
using Xunit;

namespace JacBlack.Tests.PirateBay;

/// <summary>
/// Снимок раздела HD-фильмов apibay от 29.07.2026 (100 записей).
///
/// Источник заведён ради сидов: здесь медиана 1268 и максимум 10 140,
/// тогда как у русских трекеров типичные значения — единицы и десятки.
/// Поэтому главное, что проверяется: сиды доезжают до записи целыми,
/// а magnet собирается из хеша без обращений наружу.
/// </summary>
public class PirateBayParserFixtureTests
{
    static List<PirateBayItem> Items()
        => JsonConvert.DeserializeObject<List<PirateBayItem>>(FixtureLoader.Read("PirateBay/top100_hd_movies.json"));

    [Fact]
    public void Ответ_разбирается_целиком()
    {
        var items = Items();
        Assert.True(items.Count >= 90, $"ожидали около сотни записей, получили {items.Count}");

        var torrents = PirateBayParser.ParseItems(items);
        Assert.True(torrents.Count >= 90, $"из {items.Count} записей разобрано лишь {torrents.Count}");
    }

    [Fact]
    public void Магнит_собирается_из_хеша_с_трекерами()
    {
        var torrents = PirateBayParser.ParseItems(Items());

        Assert.All(torrents, t =>
        {
            Assert.StartsWith("magnet:?xt=urn:btih:", t.magnet);
            Assert.Contains("&tr=", t.magnet);
            Assert.Equal("piratebay", t.trackerName);
        });
    }

    [Fact]
    public void Сиды_и_размер_доезжают_целыми()
    {
        var torrents = PirateBayParser.ParseItems(Items());

        // Ради этих чисел источник и заводился — если они потеряются,
        // он теряет весь смысл.
        Assert.True(torrents.Max(t => t.sid) > 1000, "самая раздаваемая должна иметь больше тысячи сидов");
        Assert.All(torrents, t =>
        {
            Assert.True(t.sid >= 0);
            Assert.True(t.size > 0, $"нулевой размер у «{t.title}»");
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName));
        });
    }

    [Fact]
    public void Имя_и_год_вытаскиваются_из_файлового_названия()
    {
        var torrents = PirateBayParser.ParseItems(Items());

        Assert.All(torrents, t => Assert.False(string.IsNullOrWhiteSpace(t.name), $"пустое имя у «{t.title}»"));

        // Названия вида Obsession.2026.1080p.AMZN — год там есть почти всегда.
        int withYear = torrents.Count(t => t.relased > 1900);
        Assert.True(withYear >= torrents.Count / 2, $"год нашёлся лишь у {withYear} из {torrents.Count}");
    }

    [Theory]
    [InlineData("207", "movie")]
    [InlineData("201", "movie")]
    [InlineData("208", "serial")]
    [InlineData("205", "serial")]
    [InlineData("206", "documovie")]
    public void Разделы_раскладываются_по_типам(string category, string expected)
    {
        Assert.Equal(new[] { expected }, PirateBayParser.TypesOf(category));
    }

    [Theory]
    [InlineData("101")]   // музыка
    [InlineData("300")]   // приложения
    [InlineData("")]
    public void Чужие_разделы_отбрасываются(string category)
    {
        Assert.Null(PirateBayParser.TypesOf(category));
    }

    [Fact]
    public void Пустой_ответ_apibay_не_создаёт_записей()
    {
        // На пустой поиск apibay отвечает одной записью-заглушкой с id «0».
        var stub = new PirateBayItem { Id = "0", Name = "No results returned", InfoHash = "0000000000000000000000000000000000000000", Category = "207" };

        Assert.Null(PirateBayParser.MapToTorrentDetails(stub));
        Assert.Null(PirateBayParser.MapToTorrentDetails(null));
        Assert.Empty(PirateBayParser.ParseItems(null));
    }
}
