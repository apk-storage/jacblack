using JacBlack.Infrastructure.Trackers.Selezen;
using Xunit;

namespace JacBlack.Tests.Selezen;

/// <summary>Эталонный снимок выдачи парсера selezen. Пояснения — в GoldenSnapshot.</summary>
public class SelezenParserGoldenTests
{
    [Fact]
    public void ParseTorrentsFromListPage_СовпадаетСЭталоном()
    {
        _ = AppInit.conf.Selezen.host;

        string html = FixtureLoader.Read("Selezen/list_page1.html");
        GoldenSnapshot.Assert("Selezen", "list_page1", SelezenParser.ParseTorrentsFromListPage(html));
    }
}
