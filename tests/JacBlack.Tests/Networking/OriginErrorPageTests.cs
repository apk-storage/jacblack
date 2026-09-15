using JacBlack.Infrastructure.Networking;
using Xunit;

namespace JacBlack.Tests.Networking;

/// <summary>
/// Страница ошибки Cloudflare «сервер сайта не ответил».
///
/// FlareSolverr отдаёт её с кодом 200, и раньше она ничем не отличалась от
/// обычной страницы. 15.09.2026 в час сбоя rutracker обходной браузер ждал по
/// 60 секунд на каждую такую — 18 раз за десять минут, и обход почти стоял.
/// Снимок снят в тот день; адрес посетителя и Ray ID заменены.
/// </summary>
public class OriginErrorPageTests
{
    [Fact]
    public void Настоящая_страница_504_опознаётся()
    {
        Assert.True(CloudflareClearance.IsOriginErrorPage(FixtureLoader.Read("Cloudflare/origin-504-rutracker.html")));
    }

    [Fact]
    public void Проверка_Cloudflare_это_не_ошибка_сервера()
    {
        // «Just a moment» — задача для браузера, а не лежащий сервер: паузу
        // на неё ставить нельзя, иначе хост выключится ровно тогда, когда его
        // надо решать.
        Assert.False(CloudflareClearance.IsOriginErrorPage(
            "<html><head><title>Just a moment...</title></head><body><div id=\"challenge-error-text\"></div></body></html>"));
    }

    [Fact]
    public void Обычная_страница_трекера_не_ошибка()
    {
        Assert.False(CloudflareClearance.IsOriginErrorPage(FixtureLoader.Read("Kinozal/browse_c8.html")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<title>Обзор: 504 лучших фильма | 2026</title>")]
    public void Похожее_на_ошибку_без_разметки_Cloudflare_не_ошибка(string html)
    {
        Assert.False(CloudflareClearance.IsOriginErrorPage(html));
    }
}
