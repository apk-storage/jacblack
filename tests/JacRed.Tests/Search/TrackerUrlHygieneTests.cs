using JacRed.Application.Search;
using Xunit;

namespace JacRed.Tests.Search;

/// <summary>
/// Подмена мёртвых доменов в ссылках на раздачи.
///
/// Замер 29.07.2026: у kinozal 69% ссылок в базе вели на `kinozal.tv`,
/// который вообще не резолвится — около 160 тысяч записей, по которым
/// человек из Лампы попадал в никуда. Правится на выдаче, поэтому
/// применяется ко всей базе сразу, без миграции.
/// </summary>
public class TrackerUrlHygieneTests
{
    [Fact]
    public void Мёртвый_домен_kinozal_меняется_на_живой()
    {
        string result = TrackerUrlHygiene.Canonical("http://kinozal.tv/details.php?id=1993242");

        Assert.Contains("kinozal.guru", result);
        Assert.DoesNotContain("kinozal.tv", result);
    }

    [Fact]
    public void Путь_и_параметры_сохраняются()
    {
        string result = TrackerUrlHygiene.Canonical("http://kinozal.tv/details.php?id=1993242");

        Assert.EndsWith("/details.php?id=1993242", result);
    }

    [Fact]
    public void Порт_по_умолчанию_в_ссылке_не_появляется()
    {
        // UriBuilder любит дописать «:80» и «:443» — в базе их не было,
        // и в выдаче они бы выглядели мусором.
        Assert.DoesNotContain(":80/", TrackerUrlHygiene.Canonical("http://kinozal.tv/details.php?id=1"));
        Assert.DoesNotContain(":443/", TrackerUrlHygiene.Canonical("https://kinozal.tv/details.php?id=1"));
    }

    [Theory]
    [InlineData("https://rutracker.net/forum/viewtopic.php?t=4238265")]
    [InlineData("https://rutor.info/torrent/123456")]
    [InlineData("https://nnmclub.to/forum/viewtopic.php?t=1681112")]
    public void Живые_домены_не_трогаем(string url)
    {
        // rutracker.net — рабочее зеркало, а не мёртвый домен. Подставлять
        // «настроенный хост трекера» вслепую нельзя: так и ломают живое.
        Assert.Equal(url, TrackerUrlHygiene.Canonical(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("не ссылка вовсе")]
    [InlineData("magnet:?xt=urn:btih:c246c69fdf3b362eeda847166ec45093648e6ba8")]
    public void Мусор_возвращается_как_есть(string url)
    {
        Assert.Equal(url, TrackerUrlHygiene.Canonical(url));
    }
}
