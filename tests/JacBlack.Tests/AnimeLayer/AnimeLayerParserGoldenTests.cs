using JacBlack.Infrastructure.Trackers.AnimeLayer;
using Xunit;

namespace JacBlack.Tests.AnimeLayer;

/// <summary>Эталонный снимок выдачи парсера animelayer. Пояснения — в GoldenSnapshot.</summary>
public class AnimeLayerParserGoldenTests
{
    [Fact]
    public void ParseTorrentListFromHtml_СовпадаетСЭталоном()
    {
        string host = AppInit.conf.Animelayer.host;

        string html = FixtureLoader.Read("AnimeLayer/list_page1.html");
        GoldenSnapshot.Assert("AnimeLayer", "list_page1", AnimeLayerParser.ParseTorrentListFromHtml(html, host, 1));
    }
}
