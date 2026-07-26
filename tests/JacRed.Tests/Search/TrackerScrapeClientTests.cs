using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using Xunit;

namespace JacRed.Tests.Search;

public class TrackerScrapeClientTests
{
    [Theory]
    [InlineData("udp://tracker.opentrackr.org:1337/announce", "tracker.opentrackr.org", 1337)]
    [InlineData("udp://open.stealth.si:80/announce", "open.stealth.si", 80)]
    [InlineData("udp://opentor.net:6969", "opentor.net", 6969)]
    [InlineData("UDP://Tracker.Example.Org:451/announce", "Tracker.Example.Org", 451)]
    public void Разбирает_udp_адреса(string url, string expectedHost, int expectedPort)
    {
        Assert.True(TrackerScrapeClient.TryParseUdp(url, out string host, out int port));
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }

    [Theory]
    [InlineData("http://retracker.local/announce")]           // не udp
    [InlineData("https://bt.t-ru.org/announce")]              // не udp
    [InlineData("udp://tracker.without.port/announce")]       // без порта
    [InlineData("udp://")]                                    // пустой
    [InlineData("")]
    [InlineData(null)]
    public void Отвергает_неподходящие_адреса(string url)
    {
        Assert.False(TrackerScrapeClient.TryParseUdp(url, out _, out _));
    }

    [Fact]
    public async Task Пустой_список_хешей_не_вызывает_сеть()
    {
        var counts = await TrackerScrapeClient.ScrapeAsync(
            "udp://tracker.opentrackr.org:1337/announce", new byte[0][], 100);

        Assert.Empty(counts);
    }

    [Fact]
    public async Task Неразбираемый_адрес_возвращает_пусто()
    {
        var counts = await TrackerScrapeClient.ScrapeAsync(
            "http://retracker.local/announce", new[] { new byte[20] }, 100);

        Assert.Empty(counts);
    }
}
