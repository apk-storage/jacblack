using System;
using System.Collections.Generic;
using JacBlack.Infrastructure.Indexers;
using JacBlack.Infrastructure.Metadata;
using JacBlack.Infrastructure.Persistence;
using JacBlack.Models.Api;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Год в запросе — условие отбора, и раздача без года из карточки выбывает.
/// Правило нужное, но наказывало оно за наш собственный разбор: сценовые
/// релизы год в названии не пишут вовсе («The.Boys.S05E08.1080p.WEB.H264»),
/// и из карточки «The Boys» вылетало 49 раздач piratebay из 51 — при том что
/// у двадцати из них стоял верный код IMDB.
///
/// Поэтому год добывается ДО отбора, тремя путями от точного к общему:
/// год сезона (TMDB), год по коду IMDB, год по коду Кинопоиска. Не нашлось —
/// раздача выбывает, как и раньше: отбор не ослаблен.
/// </summary>
public class ReleaseYearResolverTests
{
    const string BoysImdb = "tt1190634";

    // Настоящие даты сезонов «The Boys» из TMDB: карточка датирована 2019-м,
    // а пятый сезон вышел в 2026-м — на этой разнице всё и держится.
    static readonly Dictionary<int, int> BoysSeasons = new()
    {
        [1] = 2019, [2] = 2020, [3] = 2022, [4] = 2024, [5] = 2026
    };

    static Result Scene(string name, int[] seasons, string imdb = null, string kinopoisk = null) => new()
    {
        Title = $"{name} scene release",
        info = new TorrentInfo
        {
            name = name,
            originalname = name,
            relased = 0,
            seasons = seasons == null ? null : new HashSet<int>(seasons),
            types = new[] { "serial" },
            imdb = imdb,
            kinopoisk = kinopoisk
        }
    };

    [Fact]
    public void Сезонный_год_берётся_из_карты_а_не_год_премьеры()
    {
        TmdbSeasonYears.Reset();
        TmdbSeasonYears.Seed(BoysImdb, BoysSeasons, TimeSpan.FromMinutes(5));

        var results = new List<Result> { Scene("The Boys", new[] { 5 }) };

        int filled = ReleaseYearResolver.Fill(results, BoysImdb, cardIsSerial: 2);

        Assert.Equal(1, filled);
        Assert.Equal(2026, results[0].info.relased);
    }

    [Fact]
    public void У_сборника_сезонов_берётся_самый_ранний()
    {
        TmdbSeasonYears.Reset();
        TmdbSeasonYears.Seed(BoysImdb, BoysSeasons, TimeSpan.FromMinutes(5));

        // Сборник 4-5 сезонов: ранний год ближе к году карточки, а значит
        // безопаснее для отбора, чем поздний.
        var results = new List<Result> { Scene("The Boys", new[] { 5, 4 }) };

        ReleaseYearResolver.Fill(results, BoysImdb, cardIsSerial: 2);

        Assert.Equal(2024, results[0].info.relased);
    }

    [Fact]
    public void Нулевой_сезон_спецвыпусков_годом_не_считается()
    {
        TmdbSeasonYears.Reset();
        TmdbSeasonYears.Seed(BoysImdb, new Dictionary<int, int> { [0] = 2019, [5] = 2026 },
            TimeSpan.FromMinutes(5));

        var results = new List<Result> { Scene("The Boys", new[] { 0, 5 }) };

        ReleaseYearResolver.Fill(results, BoysImdb, cardIsSerial: 2);

        Assert.Equal(2026, results[0].info.relased);
    }

    [Fact]
    public void Год_берётся_по_коду_IMDB_когда_сезонов_нет()
    {
        TmdbSeasonYears.Reset();
        ImdbIndex.Remember("tt1160419", "Дюна", "Dune: Part One", 2021);

        var results = new List<Result> { Scene("Dune: Part One", null, imdb: "tt1160419") };

        ReleaseYearResolver.Fill(results, cardImdb: null, cardIsSerial: 1);

        Assert.Equal(2021, results[0].info.relased);
    }

    [Fact]
    public void Год_берётся_по_коду_Кинопоиска_для_русского_кино()
    {
        TmdbSeasonYears.Reset();
        KinopoiskIndex.Remember("326", "Побег из Шоушенка", "The Shawshank Redemption", 1994);

        var results = new List<Result> { Scene("Побег из Шоушенка", null, kinopoisk: "326") };

        ReleaseYearResolver.Fill(results, cardImdb: null, cardIsSerial: 1);

        Assert.Equal(1994, results[0].info.relased);
    }

    [Fact]
    public void Разобранный_год_не_трогаем()
    {
        TmdbSeasonYears.Reset();
        TmdbSeasonYears.Seed(BoysImdb, BoysSeasons, TimeSpan.FromMinutes(5));

        var r = Scene("The Boys", new[] { 5 });
        r.info.relased = 2019;

        int filled = ReleaseYearResolver.Fill(new List<Result> { r }, BoysImdb, cardIsSerial: 2);

        Assert.Equal(0, filled);
        Assert.Equal(2019, r.info.relased);
    }

    [Fact]
    public void Ничего_не_нашлось_год_остаётся_нулевым()
    {
        // Отбор не ослаблен: не смогли добыть год — раздача выбывает, как
        // и раньше. Это ровно тот случай, ради которого правило вводилось.
        TmdbSeasonYears.Reset();

        var results = new List<Result> { Scene("Nothing Known", new[] { 5 }) };

        int filled = ReleaseYearResolver.Fill(results, cardImdb: null, cardIsSerial: 2);

        Assert.Equal(0, filled);
        Assert.Equal(0, results[0].info.relased);
    }
}
