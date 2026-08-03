using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Trackers.Kinozal;
using Xunit;

namespace JacBlack.Tests.Kinozal;

/// <summary>Эталонный снимок выдачи парсера kinozal. Пояснения — в GoldenSnapshot.</summary>
public class KinozalParserGoldenTests
{
    public static IEnumerable<object[]> Categories() =>
        KinozalCategories.Map.Keys.OrderBy(int.Parse).Select(cat => new object[] { cat });

    [Theory]
    [MemberData(nameof(Categories))]
    public void ParseTorrentsFromPage_СовпадаетСЭталоном(string cat)
    {
        _ = AppInit.conf.Kinozal.host;

        string html = FixtureLoader.Read($"Kinozal/browse_c{cat}.html");
        GoldenSnapshot.Assert("Kinozal", $"browse_c{cat}", KinozalParser.ParseTorrentsFromPage(html, cat));
    }
}
