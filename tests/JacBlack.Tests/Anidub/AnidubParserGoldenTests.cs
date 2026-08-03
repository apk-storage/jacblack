using System;
using System.Linq;
using JacBlack.Infrastructure.Trackers.Anidub;
using Xunit;

namespace JacBlack.Tests.Anidub;

/// <summary>Эталонный снимок выдачи парсера anidub. Пояснения — в GoldenSnapshot.</summary>
public class AnidubParserGoldenTests
{
    [Fact]
    public void ParseTorrentListFromHtml_СовпадаетСЭталоном()
    {
        string host = AppInit.conf.Anidub.host;

        string html = FixtureLoader.Read("Anidub/list_page1.html");
        var parsed = AnidubParser.ParseTorrentListFromHtml(html, host, 1);

        // Часть дат парсер берёт не со страницы, а с часов: «сегодня» — это
        // текущее время, «вчера» — оно же минус сутки, и та же подстановка идёт
        // для раздач, где дату разобрать не удалось. Такие значения в эталон
        // класть нельзя — снимок разошёлся бы при первом же повторном прогоне.
        // Заменяем отметкой: сам факт подстановки сверять полезно, секунда — нет.
        var now = DateTime.UtcNow;
        bool fromClock(DateTime value) =>
            Math.Abs((now - value).TotalMinutes) < 30 ||
            Math.Abs((now.AddDays(-1) - value).TotalMinutes) < 30;

        var stable = parsed.Select(t => new
        {
            t.trackerName,
            t.types,
            t.url,
            t.title,
            t.sid,
            t.pir,
            t.sizeName,
            t.magnet,
            createTime = fromClock(t.createTime)
                ? "<подставлено с часов>"
                : t.createTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            t.name,
            t.originalname,
            t.relased
        }).ToList();

        GoldenSnapshot.AssertJson("Anidub", "list_page1", stable);
    }
}
