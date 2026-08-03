using System.Linq;
using JacBlack.Infrastructure.Trackers.Knaben;
using JacBlack.Models.tParse;
using Newtonsoft.Json;
using Xunit;

namespace JacBlack.Tests.Knaben;

/// <summary>
/// Снимок ответа api.knaben.org от 29.07.2026 (50 раздач).
///
/// Knaben — не трекер, а поисковая надстройка над чужими: TPB, 1337x, EZTV,
/// rutracker. Названия там приходят в «файловом» виде
/// (Some.Show.S01E02.2160p.WEB), и главная работа парсера — привести их
/// к имени и году, по которым потом ищет Лампа. Это и проверяем.
/// </summary>
public class KnabenParserFixtureTests
{
    static KnabenApiResponse Response()
        => JsonConvert.DeserializeObject<KnabenApiResponse>(FixtureLoader.Read("Knaben/api_page1.json"));

    [Fact]
    public void Ответ_разбирается_и_содержит_раздачи()
    {
        var response = Response();

        Assert.NotNull(response?.Hits);
        Assert.True(response.Hits.Count >= 10, $"ожидали хотя бы 10 раздач, получили {response.Hits?.Count}");
    }

    [Fact]
    public void Раздачи_раскладываются_в_записи()
    {
        var mapped = Response().Hits.Select(KnabenParser.MapToTorrentDetails).Where(t => t != null).ToList();

        Assert.NotEmpty(mapped);
        Assert.All(mapped, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.title), "пустой заголовок");
            Assert.Equal("knaben", t.trackerName);
        });
    }

    [Theory]
    [InlineData("Call the Midwife S15E08 1080p", "Call the Midwife")]
    [InlineData("War.Machine.2026.1080p.WEB", "War Machine")]
    [InlineData("Some.Movie.2019.2160p.HDR", "Some Movie")]
    public void Из_файлового_названия_достаётся_имя(string raw, string expected)
    {
        var (name, _) = KnabenParser.ParseNameAndYear(raw);

        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("War.Machine.2026.1080p.WEB", 2026)]
    [InlineData("Some.Movie.2019.2160p.HDR", 2019)]
    [InlineData("Call the Midwife S15E08 1080p", 0)]
    public void Из_файлового_названия_достаётся_год(string raw, int expected)
    {
        var (_, relased) = KnabenParser.ParseNameAndYear(raw);

        Assert.Equal(expected, relased);
    }

    [Fact]
    public void Имя_у_разобранных_записей_не_пустое()
    {
        var mapped = Response().Hits.Select(KnabenParser.MapToTorrentDetails).Where(t => t != null).ToList();

        // Имя — то, по чему запись потом найдётся в базе. Пустое имя
        // означает раздачу, которую никто никогда не увидит.
        Assert.All(mapped, t => Assert.False(string.IsNullOrWhiteSpace(t.name), $"пустое имя у «{t.title}»"));
    }

    [Fact]
    public void Пустое_название_не_роняет_разбор()
    {
        Assert.Equal((null, 0), KnabenParser.ParseNameAndYear(null));
        Assert.Equal((null, 0), KnabenParser.ParseNameAndYear(string.Empty));
        Assert.Equal((null, 0), KnabenParser.ParseNameAndYear("   "));

        // Запись без заголовка отбрасывается: искать её всё равно было бы нечем.
        Assert.Null(KnabenParser.MapToTorrentDetails(new KnabenHit { Title = null }));
    }
}
