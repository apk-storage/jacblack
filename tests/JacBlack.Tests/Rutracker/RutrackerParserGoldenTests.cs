using System.Collections.Generic;
using JacBlack.Infrastructure.Trackers.Rutracker;
using Xunit;

namespace JacBlack.Tests.Rutracker;

/// <summary>Эталонный снимок выдачи парсера rutracker. Пояснения — в GoldenSnapshot.</summary>
public class RutrackerParserGoldenTests
{
    public static IEnumerable<object[]> Forums() => new[]
    {
        new object[] { "1950" },
        new object[] { "842" },
        new object[] { "1105" },
        new object[] { "1392" },
        new object[] { "709" },
        new object[] { "24" },
    };

    [Theory]
    [MemberData(nameof(Forums))]
    public void ParseTorrentsFromPage_СовпадаетСЭталоном(string forum)
    {
        _ = AppInit.conf.Rutracker.host;

        string html = FixtureLoader.Read($"Rutracker/forum_{forum}.html");
        GoldenSnapshot.Assert("Rutracker", $"forum_{forum}", RutrackerParser.ParseTorrentsFromPage(html, forum));
    }
}
