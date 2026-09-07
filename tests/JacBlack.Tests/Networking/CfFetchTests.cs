using JacBlack.Infrastructure.Networking;
using Xunit;

namespace JacBlack.Tests.Networking;

/// <summary>
/// Быстрый путь мимо браузера: cookie, добытая браузером, плюс обычный клиент
/// с отпечатком Chrome.
///
/// Почему это важно проверять. Браузер один на всех, и когда обращений к нему
/// становится много, он занят на 101% — замер 07.09.2026: 947 запросов в час,
/// 3636 секунд работы из 3600. Глубокому обходу rutracker доставалось два
/// запроса в час, и он продвигался на одну страницу за 72 минуты при
/// оставшихся 4849.
///
/// Цена ошибки несимметрична, и отсюда всё устройство: не признали потерю
/// cookie — страница потеряна одна, следующий отказ всё равно уведёт на
/// браузер. А вот принять обычный отказ сайта за потерю cookie дорого:
/// каждая снесённая тема (404) выбрасывала бы рабочую cookie и гоняла
/// браузер заново — ровно та беда, из-за которой обход и встал.
/// </summary>
public class CfFetchTests
{
    [Fact]
    public void Отзывом_считается_только_вмешательство_Cloudflare()
    {
        // Заголовок `cf-mitigated` ставит сама Cloudflare — это и есть отзыв.
        Assert.True(CfFetch.ClearanceLost(403, "", cfMitigated: true));
        Assert.True(CfFetch.ClearanceLost(503, "", cfMitigated: true));
    }

    [Fact]
    public void Голый_403_от_самого_трекера_отзывом_НЕ_считается()
    {
        // Дорогая ошибка 07.09.2026: rutracker отвечает 403 на разделы, куда
        // нашей учётке нельзя, а обход натыкается на них постоянно. Считая
        // такой отказ отзывом cookie, служба выбрасывала рабочую clearance и
        // шла в браузер — 133 «потери» и 134 новых cookie за пять минут,
        // то есть петля, в которой обход стоял.
        Assert.False(CfFetch.ClearanceLost(403, "<html>Тема находится в закрытом разделе</html>"));
        Assert.False(CfFetch.ClearanceLost(503, "<html>Форум временно недоступен</html>"));
    }

    [Fact]
    public void Страница_с_разметкой_задачи_под_кодом_200_тоже_считается()
    {
        // Старый вид проверки приходит с кодом 200 и страницей «Just a moment…».
        Assert.True(CfFetch.ClearanceLost(200, "<html><head><title>Just a moment...</title>"));
        Assert.True(CfFetch.ClearanceLost(200, "<script>window._cf_chl_opt={};</script>"));
    }

    [Fact]
    public void Обычный_отказ_сайта_cookie_не_отменяет()
    {
        // Снесённая тема отдаёт 404 — это свойство страницы, а не поломка
        // cookie. Выбросить её здесь означало бы вернуть обход в браузер.
        Assert.False(CfFetch.ClearanceLost(404, "<html>Тема не найдена</html>"));
        Assert.False(CfFetch.ClearanceLost(200, "<html><table class=\"tCenter\">раздачи</table></html>"));
    }

    [Fact]
    public void Cookie_запоминается_и_отдаётся_по_хосту()
    {
        const string host = "fast-remember.test";
        CfFetch.Forget(host);

        Assert.Null(CfFetch.For(host));

        CfFetch.Remember(host, "cf_clearance=abc; bb_session=xyz", "Mozilla/5.0 Chrome/148");

        var got = CfFetch.For(host);
        Assert.NotNull(got);
        Assert.Equal("cf_clearance=abc; bb_session=xyz", got.Cookies);
        Assert.Equal("Mozilla/5.0 Chrome/148", got.UserAgent);
    }

    [Fact]
    public void Забытая_cookie_больше_не_отдаётся()
    {
        const string host = "fast-forget.test";

        CfFetch.Remember(host, "cf_clearance=abc", "Mozilla/5.0");
        Assert.NotNull(CfFetch.For(host));

        CfFetch.Forget(host);
        Assert.Null(CfFetch.For(host));
    }

    [Fact]
    public void Пустую_cookie_не_запоминаем()
    {
        const string host = "fast-empty.test";
        CfFetch.Forget(host);

        CfFetch.Remember(host, "", "Mozilla/5.0");
        CfFetch.Remember(host, null, "Mozilla/5.0");

        // Иначе быстрый путь пошёл бы без clearance и получал 403 на каждой
        // странице, а мы бы считали, что cookie есть.
        Assert.Null(CfFetch.For(host));
    }

    [Fact]
    public void Новый_набор_складывается_со_старым_а_не_затирает_его()
    {
        const string host = "fast-merge.test";
        CfFetch.Forget(host);

        CfFetch.Remember(host, "cf_clearance=abc; bb_session=old", "Mozilla/5.0 Chrome/148");

        // Так приходят cookie после входа на трекер: своя сессия есть,
        // а clearance в наборе может не оказаться вовсе. Замена стирала бы
        // рабочую clearance, следующий запрос ловил 403, и служба уходила в
        // браузер по кругу — 46 «потерь cookie» за пять минут 07.09.2026.
        CfFetch.Remember(host, "bb_session=new", null);

        var got = CfFetch.For(host);
        Assert.Contains("cf_clearance=abc", got.Cookies);
        Assert.Contains("bb_session=new", got.Cookies);
        Assert.DoesNotContain("bb_session=old", got.Cookies);

        // UA тоже не теряется, когда новый ответ его не назвал.
        Assert.Equal("Mozilla/5.0 Chrome/148", got.UserAgent);
    }

    [Fact]
    public void Одиночный_отказ_Cloudflare_cookie_не_выбрасывает()
    {
        const string host = "fast-mitigation.test";
        CfFetch.Reset();

        // Первые два — случайность: трекер придирается к странице поиска,
        // а обход теми же ключами идёт. Замер 07.09.2026: 47 таких отказов
        // за пять минут при ОДНОЙ настоящей задаче, найденной браузером.
        Assert.False(CfFetch.ShouldDropClearance(host));
        Assert.False(CfFetch.ShouldDropClearance(host));

        // Третий подряд — уже не случайность.
        Assert.True(CfFetch.ShouldDropClearance(host));

        // После срабатывания счёт начинается заново.
        Assert.False(CfFetch.ShouldDropClearance(host));
    }

    [Fact]
    public void Разные_хосты_не_путаются()
    {
        CfFetch.Remember("fast-a.test", "cf_clearance=a", "UA-A");
        CfFetch.Remember("fast-b.test", "cf_clearance=b", "UA-B");

        Assert.Equal("cf_clearance=a", CfFetch.For("fast-a.test").Cookies);
        Assert.Equal("cf_clearance=b", CfFetch.For("fast-b.test").Cookies);

        CfFetch.Forget("fast-a.test");

        Assert.Null(CfFetch.For("fast-a.test"));
        Assert.NotNull(CfFetch.For("fast-b.test"));
    }
}
