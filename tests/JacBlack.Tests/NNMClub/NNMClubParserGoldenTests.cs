using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Trackers.NNMClub;
using Xunit;

namespace JacBlack.Tests.NNMClub;

/// <summary>Эталонный снимок выдачи парсера nnmclub. Пояснения — в GoldenSnapshot.</summary>
public class NNMClubParserGoldenTests
{
    public static IEnumerable<object[]> Categories() =>
        NNMClubCategories.Map.Keys.OrderBy(int.Parse).Select(cat => new object[] { cat });

    [Theory]
    [MemberData(nameof(Categories))]
    public void ParseTorrentsFromPage_СовпадаетСЭталоном(string cat)
    {
        _ = AppInit.conf.NNMClub.host;

        string html = FixtureLoader.Read($"NNMClub/portal_c{cat}.html");
        GoldenSnapshot.Assert("NNMClub", $"portal_c{cat}", NNMClubParser.ParseTorrentsFromPage(html, cat));
    }
}
