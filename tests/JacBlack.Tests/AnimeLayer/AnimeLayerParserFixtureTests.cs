using System.Linq;
using JacBlack.Infrastructure.Trackers.AnimeLayer;
using Xunit;

namespace JacBlack.Tests.AnimeLayer;

/// <summary>
/// Снимок страницы animelayer.ru от 29.07.2026, снят под учётной записью
/// (гостю трекер список не отдаёт).
///
/// Последний из парсеров, остававшийся без тестов. Признак строки в вёрстке
/// у него длинный и приметный — `torrent-item torrent-item-medium panel`, —
/// поэтому проверка на тихую потерю строк здесь особенно осмысленна: смена
/// класса в вёрстке обнулит выдачу целиком и молча.
/// </summary>
public class AnimeLayerParserFixtureTests
{
    const string Host = "https://animelayer.ru";

    static string Html() => FixtureLoader.Read("AnimeLayer/list_page1.html");

    [Fact]
    public void Со_страницы_разбираются_раздачи()
    {
        var list = AnimeLayerParser.ParseTorrentListFromHtml(Html(), Host, 1);

        Assert.NotNull(list);
        Assert.True(list.Count >= 20, $"ожидали хотя бы 20 раздач, получили {list.Count}");
    }

    [Fact]
    public void У_каждой_раздачи_есть_адрес_и_заголовок()
    {
        var list = AnimeLayerParser.ParseTorrentListFromHtml(Html(), Host, 1);

        Assert.All(list, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.url), "пустой адрес");
            Assert.False(string.IsNullOrWhiteSpace(t.title), $"пустой заголовок у {t.url}");
            Assert.Equal("animelayer", t.trackerName);
        });
    }

    [Fact]
    public void Тип_раздачи_аниме()
    {
        var list = AnimeLayerParser.ParseTorrentListFromHtml(Html(), Host, 1);

        Assert.All(list, t =>
        {
            Assert.NotNull(t.types);
            Assert.Contains("anime", t.types);
        });
    }

    [Fact]
    public void Адреса_не_повторяются()
    {
        var list = AnimeLayerParser.ParseTorrentListFromHtml(Html(), Host, 1);

        Assert.Equal(list.Count, list.Select(t => t.url).Distinct().Count());
    }

    [Fact]
    public void Имя_заполнено_иначе_запись_не_найти()
    {
        var list = AnimeLayerParser.ParseTorrentListFromHtml(Html(), Host, 1);

        Assert.All(list, t => Assert.False(string.IsNullOrWhiteSpace(t.name), $"пустое имя у «{t.title}»"));
    }

    [Fact]
    public void Чужая_страница_не_роняет_разбор()
    {
        Assert.Empty(AnimeLayerParser.ParseTorrentListFromHtml("", Host, 1));
        Assert.Empty(AnimeLayerParser.ParseTorrentListFromHtml("<html>вход не выполнен</html>", Host, 1));
    }
}
