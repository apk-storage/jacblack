using JacBlack.Infrastructure.Trackers.Megapeer;
using Xunit;

namespace JacBlack.Tests.Megapeer;

/// <summary>Эталонный снимок выдачи парсера megapeer. Пояснения — в GoldenSnapshot.</summary>
public class MegapeerParserGoldenTests
{
    [Fact]
    public void ParseTorrentsFromPage_СовпадаетСЭталоном()
    {
        _ = AppInit.conf.Megapeer.host;

        string html = FixtureLoader.Read("Megapeer/browse_cat79.html");
        GoldenSnapshot.Assert("Megapeer", "browse_cat79", MegapeerParser.ParseTorrentsFromPage(html, "79"));
    }
}
