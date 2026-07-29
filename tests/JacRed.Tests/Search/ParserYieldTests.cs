using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Models.Details;
using Xunit;
using Xunit.Abstractions;

namespace JacRed.Tests.Search;

/// <summary>
/// Сторож тихой потери строк.
///
/// 29.07.2026 у Megapeer нашлось, что разбор видит 6 строк из 50: он требовал
/// от вёрстки чуть больше, чем та даёт, и лишние 44 строки пропадали без
/// единого слова в логе. Трекер год работал вполсилы и выглядел здоровым.
///
/// Этот тест ловит такую же болезнь у остальных: считает строки в снимке
/// страницы своими глазами и сравнивает с тем, сколько раздач вернул разбор.
/// Порог мягкий — часть строк отсеивается законно (реклама, закреплённые
/// темы, чужие разделы), но если разбор берёт меньше половины, это повод
/// смотреть руками.
/// </summary>
public class ParserYieldTests
{
    readonly ITestOutputHelper _output;

    public ParserYieldTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Трекер, снимок, признак раздачи на странице и наименьшая допустимая доля.
    ///
    /// Признак подобран так, чтобы считать именно раздачи, а не вёрстку.
    /// Сначала я взял разделители, по которым режет сам разбор, и получил
    /// у nnmclub «25 из 40» — но 40 это таблицы вёрстки, а раздач там было
    /// ровно 25, все разобраны. Пришлось считать по ссылке на скачивание:
    /// сторож, который врёт, хуже отсутствующего.
    /// </summary>
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "rutor", "Rutor/browse_1.html", "<tr class=\"(gai|tum)\">", 0.9 };
        yield return new object[] { "nnmclub", "NNMClub/portal_c1.html", @"download\.php\?id=\d+", 0.9 };
        yield return new object[] { "kinozal", "Kinozal/browse_c10.html", "<tr class=(?:'first bg'|bg)>", 0.9 };
        yield return new object[] { "torrentby", "TorrentBy/browse_films.html", "<tr class=\"", 0.9 };
        yield return new object[] { "megapeer", "Megapeer/browse_cat79.html", "class=\"table_fon\"", 0.9 };
        yield return new object[] { "toloka", "Toloka/f16.html", @"download\.php\?id=\d+", 0.9 };
        yield return new object[] { "selezen", "Selezen/list_page1.html", "card overflow-hidden", 0.9 };
        yield return new object[] { "anidub", "Anidub/list_page1.html", "<article", 0.9 };
        yield return new object[] { "animelayer", "AnimeLayer/list_page1.html", "torrent-item torrent-item-medium panel", 0.9 };
    }

    static List<TorrentBaseDetails> Parse(string tracker, string html)
    {
        switch (tracker)
        {
            case "rutor":
                return JacRed.Infrastructure.Trackers.Rutor.RutorParser.ParseTorrentsFromPage(html, "1");
            case "nnmclub":
                return JacRed.Infrastructure.Trackers.NNMClub.NNMClubParser.ParseTorrentsFromPage(html, "1");
            case "kinozal":
                return JacRed.Infrastructure.Trackers.Kinozal.KinozalParser.ParseTorrentsFromPage(html, "10")
                    .Cast<TorrentBaseDetails>().ToList();
            case "torrentby":
                return JacRed.Infrastructure.Trackers.TorrentBy.TorrentByParser.ParseTorrentsFromHtml(html, "films");
            case "megapeer":
                return JacRed.Infrastructure.Trackers.Megapeer.MegapeerParser.ParseTorrentsFromPage(html, "79")
                    .Cast<TorrentBaseDetails>().ToList();
            case "toloka":
                return JacRed.Infrastructure.Trackers.Toloka.TolokaParser.ParseTorrentsFromPage(html, "16")
                    .Cast<TorrentBaseDetails>().ToList();
            case "selezen":
                return JacRed.Infrastructure.Trackers.Selezen.SelezenParser.ParseTorrentsFromListPage(html)
                    .Cast<TorrentBaseDetails>().ToList();
            case "anidub":
                return JacRed.Infrastructure.Trackers.Anidub.AnidubParser
                    .ParseTorrentListFromHtml(html, "https://tr.anidub.com", 1)
                    .Cast<TorrentBaseDetails>().ToList();
            case "animelayer":
                return JacRed.Infrastructure.Trackers.AnimeLayer.AnimeLayerParser
                    .ParseTorrentListFromHtml(html, "https://animelayer.ru", 1)
                    .Cast<TorrentBaseDetails>().ToList();
            default:
                throw new ArgumentOutOfRangeException(nameof(tracker), tracker, "неизвестный трекер");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Разбор_берёт_разумную_долю_строк_страницы(string tracker, string fixture, string rowMarker, double minShare)
    {
        string html = FixtureLoader.Read(fixture);

        int rows = Regex.Matches(html, rowMarker, RegexOptions.IgnoreCase).Count;
        int parsed = Parse(tracker, html).Count;

        double share = rows == 0 ? 0 : (double)parsed / rows;
        _output.WriteLine($"{tracker}: строк {rows}, разобрано {parsed} ({share:P0})");

        Assert.True(rows > 0, $"в снимке {fixture} не нашлось ни одной строки по признаку «{rowMarker}» — снимок устарел");
        Assert.True(share >= minShare,
            $"{tracker}: разобрано {parsed} из {rows} строк ({share:P0}), ожидали хотя бы {minShare:P0}. " +
            "Так у Megapeer терялось 44 строки из 50 — проверьте вёрстку страницы");
    }
}
