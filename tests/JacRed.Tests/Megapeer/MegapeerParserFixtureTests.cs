using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.Megapeer;
using Xunit;

namespace JacRed.Tests.Megapeer;

/// <summary>
/// Снимок страницы megapeer.vip от 29.07.2026 (раздел 79, 50 строк).
///
/// Разбор был вплавлен в ParsePageAsync вместе с запросом и записью в базу —
/// проверить его снимком было нельзя, и Megapeer оставался единственным
/// трекером без тестов. Разбор вынесен в ParseTorrentsFromPage.
///
/// Magnet здесь не проверяется намеренно: на странице списка его нет,
/// он достаётся из torrent-файла отдельным запросом.
/// </summary>
public class MegapeerParserFixtureTests
{
    static string Html() => FixtureLoader.Read("Megapeer/browse_cat79.html");

    [Fact]
    public void Со_страницы_разбирается_вся_таблица_а_не_её_часть()
    {
        // В снимке 50 строк таблицы. Именно этот тест вскрыл, что разбор
        // видел только 6 из них: он требовал, чтобы за ячейкой с датой шло
        // ровно «<td>», а на живой странице у следующей ячейки есть атрибуты.
        var list = MegapeerParser.ParseTorrentsFromPage(Html(), "79");

        Assert.NotNull(list);
        Assert.True(list.Count >= 45, $"ожидали почти все 50 строк, получили {list.Count}");
    }

    [Fact]
    public void У_каждой_раздачи_есть_всё_нужное_для_загрузки()
    {
        var list = MegapeerParser.ParseTorrentsFromPage(Html(), "79");

        Assert.All(list, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.url), "пустой адрес");
            Assert.False(string.IsNullOrWhiteSpace(t.title), $"пустой заголовок у {t.url}");

            // Без downloadId magnet взять неоткуда — такая запись бесполезна.
            Assert.False(string.IsNullOrWhiteSpace(t.downloadId), $"нет downloadId у {t.url}");
            Assert.Equal("megapeer", t.trackerName);
        });
    }

    [Fact]
    public void Дата_разбирается_и_правдоподобна()
    {
        var list = MegapeerParser.ParseTorrentsFromPage(Html(), "79");

        Assert.All(list, t =>
        {
            Assert.NotEqual(default, t.createTime);
            Assert.InRange(t.createTime, new DateTime(2000, 1, 1), DateTime.UtcNow.AddDays(2));
        });
    }

    [Fact]
    public void Сиды_и_пиры_не_отрицательные()
    {
        var list = MegapeerParser.ParseTorrentsFromPage(Html(), "79");

        Assert.All(list, t =>
        {
            Assert.True(t.sid >= 0, $"отрицательные сиды у {t.url}");
            Assert.True(t.pir >= 0, $"отрицательные пиры у {t.url}");
        });
    }

    [Fact]
    public void Адреса_не_повторяются()
    {
        var list = MegapeerParser.ParseTorrentsFromPage(Html(), "79");

        Assert.Equal(list.Count, list.Select(t => t.url).Distinct().Count());
    }

    [Fact]
    public void Чужая_страница_отбрасывается_целиком()
    {
        // Признак настоящей страницы — метка вёрстки. Без неё это заглушка
        // провайдера или страница ошибки, и разбирать её нельзя.
        Assert.Empty(MegapeerParser.ParseTorrentsFromPage("<html>Доступ ограничен</html>", "79"));
        Assert.Empty(MegapeerParser.ParseTorrentsFromPage(null, "79"));
        Assert.Empty(MegapeerParser.ParseTorrentsFromPage("", "79"));
    }
}
