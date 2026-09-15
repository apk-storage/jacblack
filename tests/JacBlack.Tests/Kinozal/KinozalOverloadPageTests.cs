using JacBlack.Infrastructure.Networking;
using JacBlack.Infrastructure.Trackers.Kinozal;
using Xunit;

namespace JacBlack.Tests.Kinozal;

/// <summary>
/// Заглушка перегруженного kinozal — не потеря входа.
///
/// 15.09.2026 обход kinozal входил заново каждые три минуты: страница
/// «Сервер находится под высокой нагрузкой» приходила с кодом 200 вместо
/// листинга, в ней нет ссылки «Выход», и код считал, что вход потерян.
/// Cookie входа при этом были целы, а заглушку видели все — и Contabo1,
/// который kinozal не обходит.
/// </summary>
public class KinozalOverloadPageTests
{
    // Тело ответа дословно, снято через браузер 15.09.2026.
    const string Заглушка =
        "<html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=windows-1251\">" +
        "<meta http-equiv=\"refresh\" content=\"30 /details.php?id=1161986\"></head><body>" +
        "<table border=\"0\" width=\"400\" height=\"100%\" align=\"center\"><tbody><tr><td bgcolor=\"green\">" +
        "<font style=\"font-size:15px\"><b>Сервер находится под высокой нагрузкой, очевидно в связи с очередной атакой. " +
        "Пожалуйста, подождите несколько минут и возвращайтесь к Нам в<br><font color=\"green\">Кинозал.GURU</font>" +
        "<br><br>Форум проекта всегда доступен для Вас <a href=\"https://forum.kinozal.guru/\" target=\"_blank\">здесь</a>.</b>" +
        "</font></td></tr></tbody></table></body></html>";

    [Fact]
    public void Заглушка_опознаётся()
    {
        Assert.True(KinozalSyncService.IsOverloadPage(Заглушка));
    }

    [Fact]
    public void Настоящий_листинг_не_заглушка()
    {
        Assert.False(KinozalSyncService.IsOverloadPage(FixtureLoader.Read("Kinozal/browse_c8.html")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><title>Just a moment...</title></html>")]
    public void Пустое_и_проверка_Cloudflare_не_заглушка(string html)
    {
        Assert.False(KinozalSyncService.IsOverloadPage(html));
    }

    [Fact]
    public void Пауза_ставится_и_без_ответа_HTTP()
    {
        const string host = "kinozal-overload.test";
        HostThrottle.Reset(host);

        HostThrottle.Throttled(host, null, "страница «высокая нагрузка»");

        // Без Retry-After первая пауза — базовые 15 секунд.
        Assert.InRange(HostThrottle.Remaining(host).TotalSeconds, 10, 15.5);
    }
}
