using JacBlack.Infrastructure.Trackers.Selezen;
using Xunit;

namespace JacBlack.Tests.Selezen;

/// <summary>
/// Признак «страница отдана под входом» у selezen.
///
/// С 14.09.2026 обход не приносил ничего: сессия короткая, cookie хранилась
/// сутки, и после нового входа страница со свежей cookie не повторялась.
/// Повтор решается по этому признаку, поэтому он и закреплён.
/// </summary>
public class SelezenLoggedInTests
{
    static string Листинг() => FixtureLoader.Read("Selezen/list_page1.html");

    [Fact]
    public void Гостевой_листинг_без_имени_учётки()
    {
        Assert.False(SelezenSyncService.LoggedIn(Листинг(), "jacblack"));
    }

    [Fact]
    public void Листинг_с_именем_учётки_под_входом()
    {
        string html = Листинг().Replace("</body>", "<a href=\"/user/jacblack/\">jacblack</a></body>");
        Assert.True(SelezenSyncService.LoggedIn(html, "jacblack"));
    }

    [Theory]
    [InlineData(null, "jacblack")]
    [InlineData("", "jacblack")]
    [InlineData("<html>>jacblack<</html>", "jacblack")]
    public void Не_страница_сайта_не_вход(string html, string user)
    {
        // Последний случай: имя есть, но это не страница DLE (нет dle_root).
        Assert.False(SelezenSyncService.LoggedIn(html, user));
    }

    [Fact]
    public void Без_логина_в_конфиге_входом_не_считается()
    {
        Assert.False(SelezenSyncService.LoggedIn(Листинг(), null));
    }
}
