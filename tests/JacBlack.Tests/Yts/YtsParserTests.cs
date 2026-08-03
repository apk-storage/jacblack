using System.Linq;
using JacBlack.Infrastructure.Trackers.Yts;
using Newtonsoft.Json;
using Xunit;

namespace JacBlack.Tests.Yts;

/// <summary>
/// Разбор ответа YTS на снимке живого API.
///
/// Главная особенность источника: один фильм приходит с несколькими вариантами
/// качества, и каждый должен стать ОТДЕЛЬНОЙ раздачей со своим адресом. Пока
/// адрес был общим, варианты затирали друг друга в базе — проверено 30.07.2026
/// на живых данных, из ста фильмов сохранялось сто записей вместо двухсот
/// двадцати. Тест ниже сторожит именно это.
/// </summary>
public class YtsParserTests
{
    static YtsResponse Fixture() =>
        JsonConvert.DeserializeObject<YtsResponse>(FixtureLoader.Read("Yts/list_page1.json"));

    [Fact]
    public void Снимок_разбирается_в_раздачи()
    {
        var torrents = YtsParser.ParseMovies(Fixture().Data.Movies);

        Assert.NotEmpty(torrents);
        Assert.All(torrents, t =>
        {
            Assert.Equal("yts", t.trackerName);
            Assert.Equal(new[] { "movie" }, t.types);
            Assert.False(string.IsNullOrWhiteSpace(t.title));
            Assert.False(string.IsNullOrWhiteSpace(t.name));
            Assert.StartsWith("magnet:", t.magnet);
        });
    }

    [Fact]
    public void Каждый_вариант_качества_получает_свой_адрес()
    {
        var movies = Fixture().Data.Movies;
        var torrents = YtsParser.ParseMovies(movies);

        int variants = movies.Sum(m => m.Torrents?.Length ?? 0);

        Assert.Equal(variants, torrents.Count);
        Assert.Equal(torrents.Count, torrents.Select(t => t.url).Distinct().Count());
    }

    [Fact]
    public void Качество_попадает_в_заголовок()
    {
        // Иначе варианты одного фильма неразличимы в выдаче.
        var torrents = YtsParser.ParseMovies(Fixture().Data.Movies);

        Assert.Contains(torrents, t => t.title.Contains("1080p"));
        Assert.Contains(torrents, t => t.title.Contains("720p"));
    }

    [Fact]
    public void Код_IMDB_общий_у_вариантов_одного_фильма()
    {
        var movie = Fixture().Data.Movies.First(m => m.Torrents != null && m.Torrents.Length > 1);
        var torrents = YtsParser.ParseMovies(new[] { movie });

        Assert.True(torrents.Count > 1);
        Assert.Single(torrents.Select(t => t.imdb).Distinct());
        Assert.StartsWith("tt", torrents[0].imdb);
    }

    [Fact]
    public void Год_берётся_из_поля_а_не_из_заголовка()
    {
        var torrents = YtsParser.ParseMovies(Fixture().Data.Movies);

        Assert.All(torrents, t => Assert.True(t.relased > 1900 && t.relased < 2100,
            $"год {t.relased} у «{t.title}»"));
    }
}
