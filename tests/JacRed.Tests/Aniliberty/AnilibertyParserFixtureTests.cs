using System.Linq;
using JacRed.Infrastructure.Trackers.Aniliberty;
using JacRed.Models.tParse;
using Newtonsoft.Json;
using Xunit;

namespace JacRed.Tests.Aniliberty;

/// <summary>
/// Снимок ответа API aniliberty.top от 29.07.2026 (50 раздач).
///
/// У этого источника разбора HTML нет вовсе — приходит JSON, поэтому тесты
/// проверяют раскладку полей ответа в наши записи: адрес, magnet, размер,
/// тип раздачи. Именно здесь легче всего молча потерять поле при обновлении
/// схемы на их стороне.
/// </summary>
public class AnilibertyParserFixtureTests
{
    const string Host = "https://aniliberty.top";

    static AnilibertyApiResponse Response()
        => JsonConvert.DeserializeObject<AnilibertyApiResponse>(FixtureLoader.Read("Aniliberty/torrents_page1.json"));

    [Fact]
    public void Ответ_разбирается_и_содержит_раздачи()
    {
        var response = Response();

        Assert.NotNull(response);
        Assert.NotNull(response.Data);
        Assert.True(response.Data.Count >= 10, $"ожидали хотя бы 10 раздач, получили {response.Data?.Count}");
    }

    [Fact]
    public void Раздачи_раскладываются_в_записи_с_магнитом()
    {
        var list = AnilibertyParser.MapPageTorrents(Response(), Host);

        Assert.NotEmpty(list);
        Assert.All(list, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.url), "пустой адрес");
            Assert.False(string.IsNullOrWhiteSpace(t.magnet), $"нет magnet у {t.url}");
            Assert.StartsWith("magnet:", t.magnet);
            Assert.False(string.IsNullOrWhiteSpace(t.title), $"пустой заголовок у {t.url}");
            Assert.Equal("aniliberty", t.trackerName);
        });
    }

    [Fact]
    public void У_раздач_проставлен_тип_и_он_из_известного_набора()
    {
        var list = AnilibertyParser.MapPageTorrents(Response(), Host);
        string[] known = { "anime", "multfilm", "multserial", "movie", "serial" };

        Assert.All(list, t =>
        {
            Assert.NotNull(t.types);
            Assert.NotEmpty(t.types);
            Assert.All(t.types, type => Assert.Contains(type, known));
        });
    }

    [Fact]
    public void Размер_положительный_и_подписан()
    {
        var list = AnilibertyParser.MapPageTorrents(Response(), Host);

        Assert.All(list.Where(t => t.size > 0), t =>
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName), $"размер без подписи у {t.url}"));
    }

    [Fact]
    public void Адреса_не_повторяются()
    {
        var list = AnilibertyParser.MapPageTorrents(Response(), Host);

        Assert.Equal(list.Count, list.Select(t => t.url).Distinct().Count());
    }

    [Fact]
    public void Пустой_ответ_не_роняет_разбор()
    {
        Assert.Empty(AnilibertyParser.MapPageTorrents(null, Host));
        Assert.Empty(AnilibertyParser.MapPageTorrents(new AnilibertyApiResponse(), Host));
    }
}
