using JacRed.Infrastructure.Trackers.Anidub;
using Xunit;

namespace JacRed.Tests.Anidub;

/// <summary>Эталонный снимок выдачи парсера anidub. Пояснения — в GoldenSnapshot.</summary>
public class AnidubParserGoldenTests
{
    [Fact]
    public void ParseTorrentListFromHtml_СовпадаетСЭталоном()
    {
        string host = AppInit.conf.Anidub.host;

        string html = FixtureLoader.Read("Anidub/list_page1.html");
        GoldenSnapshot.Assert("Anidub", "list_page1", AnidubParser.ParseTorrentListFromHtml(html, host, 1));
    }
}
