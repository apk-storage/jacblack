using JacRed.Infrastructure.Trackers.Toloka;
using Xunit;

namespace JacRed.Tests.Toloka;

/// <summary>Эталонный снимок выдачи парсера toloka. Пояснения — в GoldenSnapshot.</summary>
public class TolokaParserGoldenTests
{
    [Fact]
    public void ParseTorrentsFromPage_СовпадаетСЭталоном()
    {
        _ = AppInit.conf.Toloka.host;

        string html = FixtureLoader.Read("Toloka/f16.html");
        GoldenSnapshot.Assert("Toloka", "f16", TolokaParser.ParseTorrentsFromPage(html, "16"));
    }
}
