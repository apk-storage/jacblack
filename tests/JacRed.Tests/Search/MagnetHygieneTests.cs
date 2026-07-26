using System.Linq;
using JacRed.Application.Search;
using Xunit;

namespace JacRed.Tests.Search;

public class MagnetHygieneTests
{
    const string Hash = "c246c69fdf3b362eeda847166ec45093648e6ba8";

    static string Magnet(params string[] trackers)
    {
        string m = $"magnet:?xt=urn:btih:{Hash}&dn=Test";
        foreach (var t in trackers)
            m += "&tr=" + System.Web.HttpUtility.UrlEncode(t);
        return m;
    }

    [Fact]
    public void Вырезает_внутрисетевой_ретрекер()
    {
        var cleaned = MagnetHygiene.Clean(Magnet("http://retracker.local/announce", "udp://opentor.net:6969/announce"));

        Assert.DoesNotContain("retracker.local", cleaned);
        Assert.Contains("opentor.net", cleaned);
    }

    [Fact]
    public void Хеш_и_имя_переживают_чистку()
    {
        var cleaned = MagnetHygiene.Clean(Magnet("http://retracker.local/announce", "udp://opentor.net:6969/announce"));

        Assert.Contains(Hash, cleaned);
        Assert.Contains("dn=Test", cleaned);
    }

    [Fact]
    public void Ссылке_без_трекеров_они_дописываются()
    {
        // Так выглядят все раздачи kinozal и nnmclub — 262 тысячи записей.
        var cleaned = MagnetHygiene.Clean($"magnet:?xt=urn:btih:{Hash}&dn=Test");

        Assert.Contains("tr=", cleaned);
        Assert.Contains("opentrackr.org", cleaned);
    }

    [Fact]
    public void Если_после_чистки_ничего_не_осталось_дописываем()
    {
        var cleaned = MagnetHygiene.Clean(Magnet("http://retracker.local/announce"));

        Assert.DoesNotContain("retracker.local", cleaned);
        Assert.Contains("tr=", cleaned);
    }

    [Fact]
    public void Здоровая_ссылка_не_трогается()
    {
        string src = Magnet("udp://opentor.net:6969/announce", "udp://bt.t-ru.org:2710/announce");

        Assert.Equal(src, MagnetHygiene.Clean(src));
    }

    [Fact]
    public void Мусор_на_входе_возвращается_как_есть()
    {
        Assert.Equal("не ссылка вовсе", MagnetHygiene.Clean("не ссылка вовсе"));
        Assert.Null(MagnetHygiene.Clean(null));
        Assert.Equal("", MagnetHygiene.Clean(""));
    }

    [Fact]
    public void Список_трекеров_для_опроса_чистый()
    {
        var urls = MagnetHygiene.AnnounceUrls(Magnet("http://retracker.local/announce", "udp://opentor.net:6969/announce"));

        Assert.Single(urls);
        Assert.Contains("opentor.net", urls[0]);
    }

    [Fact]
    public void Для_ссылки_без_трекеров_опрашивать_есть_кого()
    {
        var urls = MagnetHygiene.AnnounceUrls($"magnet:?xt=urn:btih:{Hash}");

        Assert.NotEmpty(urls);
        Assert.All(urls, u => Assert.StartsWith("udp://", u));
    }

    [Fact]
    public void Дубли_трекеров_схлопываются()
    {
        var urls = MagnetHygiene.AnnounceUrls(Magnet("udp://opentor.net:6969/announce", "udp://opentor.net:6969/announce"));

        Assert.Single(urls);
    }
}
