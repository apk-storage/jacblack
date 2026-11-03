using System;
using JacBlack.Infrastructure.Trackers.Kinozal;
using Xunit;

namespace JacBlack.Tests.Kinozal;

/// <summary>
/// Разбор details.php для добора свежего по номерам. Фикстуры сняты 03.11.2026
/// сессией службы, следы учётки вычищены.
/// </summary>
public class KinozalDetailsPageTests
{
    public KinozalDetailsPageTests()
    {
        _ = AppInit.conf.Kinozal.host;
    }

    [Fact]
    public void ParseDetailsPage_Serial_FieldsFromPage()
    {
        var t = KinozalParser.ParseDetailsPage(FixtureLoader.Read("Kinozal/details_2159643.html"));

        Assert.NotNull(t);
        Assert.Equal($"{AppInit.conf.Kinozal.host}/details.php?id=2159643", t.url);
        Assert.StartsWith("Идеальная работа (1 сезон: 1 серия из 8) / Ggoolalba", t.title);
        Assert.Equal("Идеальная работа", t.name);
        Assert.Equal("2.15 ГБ", t.sizeName);
        Assert.Equal(new[] { "serial" }, t.types);
        Assert.Equal(0, t.sid);
        Assert.Equal(2, t.pir);
        Assert.Equal(new TimeSpan(16, 21, 0), t.createTime.TimeOfDay);
    }

    [Fact]
    public void ParseDetailsPage_SameUrlAsListing()
    {
        // Ключ записи — ссылка: добор и список должны давать одну и ту же,
        // иначе одна раздача ляжет дважды.
        var t = KinozalParser.ParseDetailsPage(FixtureLoader.Read("Kinozal/details_2159650.html"));
        Assert.NotNull(t);
        Assert.Equal($"{AppInit.conf.Kinozal.host}/details.php?id=2159650", t.url);
        Assert.Equal("26.14 ГБ", t.sizeName);
    }

    [Fact]
    public void MissingPage_IsMissing_AndNotParsed()
    {
        string html = FixtureLoader.Read("Kinozal/details_missing.html");
        Assert.True(KinozalParser.IsMissingDetailsPage(html));
        Assert.Null(KinozalParser.ParseDetailsPage(html));
        Assert.False(KinozalParser.IsMissingDetailsPage(FixtureLoader.Read("Kinozal/details_2159643.html")));
    }

    [Fact]
    public void ParseDetailsPage_ForeignCategory_Null()
    {
        string html = FixtureLoader.Read("Kinozal/details_2159643.html").Replace("onclick=\"cat(46);", "onclick=\"cat(3);");
        Assert.Null(KinozalParser.ParseDetailsPage(html));
    }

    [Theory]
    [InlineData("31 октября 2026 в 05:40", 2026, 10, 31, 5, 40)]
    [InlineData("1 ноября 2026 в 23:05", 2026, 11, 1, 23, 5)]
    [InlineData("31.10.2026 в 05:40", 2026, 10, 31, 5, 40)]
    public void ParseDetailsPage_DateWithMonthName(string raw, int y, int mo, int d, int h, int mi)
    {
        // 03.11.2026 первый проход добора засчитал 1 раздачу из 25: у всех
        // старше вчерашней дата словами, и страница выбрасывалась.
        string html = FixtureLoader.Read("Kinozal/details_2159643.html")
            .Replace("сегодня в 16:21", raw);
        var t = KinozalParser.ParseDetailsPage(html);
        Assert.NotNull(t);
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc), t.createTime);
    }

    [Fact]
    public void ParseDetailsPage_UpdatedWins()
    {
        string html = FixtureLoader.Read("Kinozal/details_2159643.html")
            .Replace("<li>Залит<span class=\"floatright green n\">сегодня в 16:21</span></li>",
                     "<li>Залит<span class=\"floatright green n\">01.11.2026 в 10:00</span></li><li>Обновлен<span class=\"floatright green n\">02.11.2026 в 12:30</span></li>");
        var t = KinozalParser.ParseDetailsPage(html);
        Assert.NotNull(t);
        Assert.Equal(new DateTime(2026, 11, 2, 12, 30, 0, DateTimeKind.Utc), t.createTime);
    }
}
