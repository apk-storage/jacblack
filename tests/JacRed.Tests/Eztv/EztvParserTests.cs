using System.Linq;
using JacRed.Infrastructure.Trackers.Eztv;
using Newtonsoft.Json;
using Xunit;

namespace JacRed.Tests.Eztv;

/// <summary>
/// Разбор ответа EZTV на снимке живого API.
///
/// Источник заводился ради кода IMDB: он приходит вместе с раздачей и позволяет
/// отвечать на поиск по идентификатору из своей базы, без чужого сервиса.
/// </summary>
public class EztvParserTests
{
    static EztvResponse Fixture() =>
        JsonConvert.DeserializeObject<EztvResponse>(FixtureLoader.Read("Eztv/list_page1.json"));

    [Fact]
    public void Снимок_разбирается_в_раздачи()
    {
        var torrents = EztvParser.ParseItems(Fixture().Torrents);

        Assert.NotEmpty(torrents);
        Assert.All(torrents, t =>
        {
            Assert.Equal("eztv", t.trackerName);
            Assert.Equal(new[] { "serial" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.False(string.IsNullOrWhiteSpace(t.url));
            Assert.StartsWith("magnet:", t.magnet);
        });
    }

    [Fact]
    public void Код_IMDB_доходит_до_записи()
    {
        var torrents = EztvParser.ParseItems(Fixture().Torrents);
        var withImdb = torrents.Where(t => !string.IsNullOrWhiteSpace(t.imdb)).ToList();

        // У части раздач источник кода не знает — это нормально. Но у большинства он есть.
        Assert.True(withImdb.Count > torrents.Count / 2,
            $"код IMDB нашёлся лишь у {withImdb.Count} из {torrents.Count}");

        Assert.All(withImdb, t => Assert.StartsWith("tt", t.imdb));
    }

    [Theory]
    [InlineData("31596422", "tt31596422")]
    [InlineData("tt1234567", "tt1234567")]
    [InlineData("TT1234567", "tt1234567")]
    [InlineData("0", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Код_приводится_к_общепринятому_виду(string raw, string expected)
    {
        // EZTV отдаёт код БЕЗ префикса — «31596422». Лампа и TMDB ждут «tt31596422».
        Assert.Equal(expected, EztvParser.NormalizeImdb(raw));
    }

    [Fact]
    public void Размер_и_сиды_переносятся()
    {
        var torrents = EztvParser.ParseItems(Fixture().Torrents);

        Assert.Contains(torrents, t => t.size > 0);
        Assert.All(torrents, t => Assert.True(t.sid >= 0 && t.pir >= 0));
    }
}
