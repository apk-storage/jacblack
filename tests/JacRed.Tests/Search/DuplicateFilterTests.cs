using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Application.Search;
using JacRed.Models.Details;
using Xunit;

namespace JacRed.Tests.Search;

/// <summary>
/// Повторы одной раздачи внутри трекера.
///
/// Берутся из-за смены домена: адрес раздачи — ключ записи, поэтому одна
/// тема rutracker лежит и под `.net`, и под `.org`. На выдаче по «Аватару»
/// таких было 36 из 492.
/// </summary>
public class DuplicateFilterTests
{
    const string HashA = "e32c5a41daa88b09dfa44eeb2fd4b32375cc54fb";
    const string HashB = "2f1b54bfdb74648eba0b752c6e48f5e87d4c9563";

    static TorrentDetails T(string tracker, string hash, int sid, string url, DateTime? updated = null) => new()
    {
        trackerName = tracker,
        magnet = "magnet:?xt=urn:btih:" + hash,
        sid = sid,
        url = url,
        updateTime = updated ?? new DateTime(2026, 7, 1)
    };

    static List<TorrentDetails> Filter(params TorrentDetails[] items)
        => DuplicateFilter.RemoveSameTrackerDuplicates(items, t => t);

    [Fact]
    public void Повтор_внутри_трекера_схлопывается()
    {
        var result = Filter(
            T("rutracker", HashA, 4, "https://rutracker.net/forum/viewtopic.php?t=254461"),
            T("rutracker", HashA, 6, "https://rutracker.org/forum/viewtopic.php?t=254461"));

        Assert.Single(result);
    }

    [Fact]
    public void Из_повторов_остаётся_запись_с_бОльшим_числом_сидов()
    {
        var result = Filter(
            T("rutracker", HashA, 4, "https://rutracker.net/forum/viewtopic.php?t=254461"),
            T("rutracker", HashA, 6, "https://rutracker.org/forum/viewtopic.php?t=254461"));

        Assert.Equal(6, result[0].sid);
        Assert.Contains("rutracker.org", result[0].url);
    }

    [Fact]
    public void При_равных_сидах_остаётся_более_свежая()
    {
        var result = Filter(
            T("rutracker", HashA, 5, "старая", new DateTime(2026, 1, 1)),
            T("rutracker", HashA, 5, "свежая", new DateTime(2026, 7, 20)));

        Assert.Equal("свежая", result[0].url);
    }

    [Fact]
    public void Одна_раздача_на_разных_трекерах_остаётся_обеими()
    {
        // У них разные страницы и разные сиды — человеку полезно видеть оба.
        var result = Filter(
            T("rutracker", HashA, 4, "https://rutracker.org/forum/viewtopic.php?t=254461"),
            T("rutor", HashA, 9, "https://rutor.info/torrent/123456"));

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Разные_раздачи_не_схлопываются()
    {
        var result = Filter(
            T("rutracker", HashA, 4, "a"),
            T("rutracker", HashB, 9, "b"));

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Записи_без_хеша_проходят_как_есть()
    {
        var noMagnet = new TorrentDetails { trackerName = "rutracker", url = "нет магнита" };
        var result = DuplicateFilter.RemoveSameTrackerDuplicates(new[] { noMagnet, noMagnet }, t => t);

        // Сравнивать нечем — молча выбрасывать такие записи нельзя.
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Пустой_список_не_роняет()
    {
        Assert.Empty(DuplicateFilter.RemoveSameTrackerDuplicates(Array.Empty<TorrentDetails>(), t => t));
    }
}
