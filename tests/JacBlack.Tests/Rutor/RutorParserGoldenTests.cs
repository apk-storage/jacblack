using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Trackers.Rutor;
using Xunit;

namespace JacBlack.Tests.Rutor;

/// <summary>Эталонный снимок выдачи парсера rutor. Пояснения — в GoldenSnapshot.</summary>
public class RutorParserGoldenTests
{
    public static IEnumerable<object[]> Categories() =>
        RutorCategories.Map.Keys.OrderBy(int.Parse).Select(cat => new object[] { cat });

    [Theory]
    [MemberData(nameof(Categories))]
    public void ParseTorrentsFromPage_СовпадаетСЭталоном(string cat)
    {
        string html = FixtureLoader.Read($"Rutor/browse_{cat}.html");
        GoldenSnapshot.Assert("Rutor", $"browse_{cat}", RutorParser.ParseTorrentsFromPage(html, cat));
    }
}
