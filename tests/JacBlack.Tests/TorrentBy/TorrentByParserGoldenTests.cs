using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Trackers.TorrentBy;
using Xunit;

namespace JacBlack.Tests.TorrentBy;

/// <summary>Эталонный снимок выдачи парсера torrentby. Пояснения — в GoldenSnapshot.</summary>
public class TorrentByParserGoldenTests
{
    public static IEnumerable<object[]> Categories() =>
        TorrentByCategories.Map.Keys.OrderBy(k => k).Select(cat => new object[] { cat });

    [Theory]
    [MemberData(nameof(Categories))]
    public void ParseTorrentsFromPage_СовпадаетСЭталоном(string cat)
    {
        _ = AppInit.conf.TorrentBy.host;

        string html = FixtureLoader.Read($"TorrentBy/browse_{cat}.html");
        GoldenSnapshot.Assert("TorrentBy", $"browse_{cat}", TorrentByParser.ParseTorrentsFromHtml(html, cat));
    }
}
