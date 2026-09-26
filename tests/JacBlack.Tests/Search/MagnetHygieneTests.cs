using System.Linq;
using JacBlack.Application.Search;
using Xunit;

namespace JacBlack.Tests.Search;

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
    public void Ссылка_без_трекеров_опрашивается_только_подставленными()
    {
        // Так хранятся раздачи rutracker, kinozal и nnmclub.
        Assert.True(MagnetHygiene.HasOnlySubstitutedTrackers($"magnet:?xt=urn:btih:{Hash}&dn=Test"));
    }

    [Fact]
    public void Вырезанный_свой_трекер_тоже_значит_подставленные()
    {
        Assert.True(MagnetHygiene.HasOnlySubstitutedTrackers(Magnet("http://retracker.local/announce")));
    }

    [Fact]
    public void Свой_трекер_раздачи_не_подставленный()
    {
        // rutor живёт на opentor — это его собственный трекер.
        Assert.False(MagnetHygiene.HasOnlySubstitutedTrackers(Magnet("udp://opentor.net:6969/announce")));
        Assert.False(MagnetHygiene.HasOnlySubstitutedTrackers(Magnet("http://retracker.local/announce", "udp://bt.t-ru.org:2710/announce")));
    }

    [Fact]
    public void Мусор_не_считается_подставленным()
    {
        Assert.False(MagnetHygiene.HasOnlySubstitutedTrackers("не ссылка вовсе"));
        Assert.False(MagnetHygiene.HasOnlySubstitutedTrackers(null));
    }

    [Theory]
    // Свой трекер говорит правду в обе стороны — ноль тоже ответ.
    [InlineData(false, 0, 50, true)]
    [InlineData(false, 3, 50, true)]
    [InlineData(false, 80, 50, true)]
    // Подставленный — только вверх: ноль и меньшее число значат «не вижу».
    [InlineData(true, 0, 50, false)]
    [InlineData(true, 3, 50, false)]
    [InlineData(true, 50, 50, false)]
    [InlineData(true, 80, 50, true)]
    // В базе ноль: любой живой ответ — правда, нулевой — нет.
    [InlineData(true, 1, 0, true)]
    [InlineData(true, 0, 0, false)]
    // Отрицательное в базе («нет данных») не мешает принять живое число.
    [InlineData(true, 2, -1, true)]
    public void Ответ_подставленных_трекеров_учитывается_только_вверх(bool substituted, int answered, int stored, bool counts)
    {
        Assert.Equal(counts, MagnetHygiene.AnswerCounts(substituted, answered, stored));
    }

    [Fact]
    public void Дубли_трекеров_схлопываются()
    {
        var urls = MagnetHygiene.AnnounceUrls(Magnet("udp://opentor.net:6969/announce", "udp://opentor.net:6969/announce"));

        Assert.Single(urls);
    }
}
