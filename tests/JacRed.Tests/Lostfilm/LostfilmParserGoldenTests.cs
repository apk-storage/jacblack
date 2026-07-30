using System.Collections.Generic;
using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Lostfilm;
using JacRed.Models.Details;
using Xunit;

namespace JacRed.Tests.Lostfilm;

/// <summary>
/// Эталонные снимки выдачи парсера lostfilm. Пояснения — в GoldenSnapshot.
///
/// У lostfilm разбор разнесён по четырём файлам и точек входа несколько,
/// поэтому снимков тоже несколько — по одному на точку.
/// </summary>
public class LostfilmParserGoldenTests
{
    const string Host = "https://www.lostfilm.tv";

    [Fact]
    public void ParseNewPageDates_СовпадаетСЭталоном()
    {
        string html = FixtureLoader.Read("Lostfilm/new_page1.html");
        GoldenSnapshot.AssertJson("Lostfilm", "new_page_dates", LostfilmParser.ParseNewPageDates(html, Host));
    }

    [Fact]
    public void BuildHorBreakerNameMap_СовпадаетСЭталоном()
    {
        string html = FixtureLoader.Read("Lostfilm/new_page1.html");
        GoldenSnapshot.AssertJson("Lostfilm", "hor_breaker_names", LostfilmParser.BuildHorBreakerNameMap(html));
    }

    [Fact]
    public void ParseVPageQualityLinkUrls_СовпадаетСЭталоном()
    {
        string html = FixtureLoader.Read("Lostfilm/v_page_qualities.html");
        GoldenSnapshot.AssertJson("Lostfilm", "v_page_qualities", LostfilmParser.ParseVPageQualityLinkUrls(html));
    }

    [Fact]
    public async Task CollectFromEpisodeLinks_СовпадаетСЭталоном()
    {
        string html = FixtureLoader.Read("Lostfilm/new_page1.html");
        var map = LostfilmParser.BuildHorBreakerNameMap(html);

        var list = new List<TorrentDetails>();
        await LostfilmParser.CollectFromEpisodeLinks(html, Host, cookie: null, list, page: 1, map);
        LostfilmParser.DedupeListByUrl(list);

        GoldenSnapshot.Assert("Lostfilm", "episode_links", list);
    }
}
