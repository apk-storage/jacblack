using System.Net;
using System.Net.Http;
using JacBlack.Infrastructure.Networking;
using Xunit;

namespace JacBlack.Tests.Networking;

/// <summary>
/// Отличаем вызов Cloudflare от обычного отказа трекера.
///
/// Цена ошибки несимметрична. Не распознали проверку — потеряли одну
/// страницу. Приняли обычный отказ за проверку — хост уходит в браузер
/// на часы, и обход встаёт целиком: так 30.07.2026 застрял nnmclub, у него
/// глубокий обход свалился с «разобрано 834» до «разобрано 1, не вышло 239».
///
/// Причина была в признаке: считалось «403 или 503 плюс заголовок cf-ray».
/// Но cf-ray Cloudflare ставит на КАЖДЫЙ ответ любого сайта за ней —
/// проверено 31.07.2026, он пришёл вместе с успешной страницей nnmclub.
/// </summary>
public class ChallengeDetectionTests
{
    static HttpResponseMessage Response(HttpStatusCode code, params (string name, string value)[] headers)
    {
        var r = new HttpResponseMessage(code);
        foreach (var (name, value) in headers)
            r.Headers.TryAddWithoutValidation(name, value);
        return r;
    }

    [Fact]
    public void Cf_ray_сам_по_себе_проверкой_не_считается()
    {
        // Тот самый случай: трекер за Cloudflare перегружен и отдаёт 503,
        // а cf-ray стоит просто потому, что он за Cloudflare.
        Assert.False(IsChallenge(Response(HttpStatusCode.ServiceUnavailable, ("cf-ray", "a23b3a1cf9f8dbcb-FRA"))));
        Assert.False(IsChallenge(Response(HttpStatusCode.Forbidden, ("cf-ray", "a23b3a1cf9f8dbcb-FRA"))));
    }

    [Fact]
    public void Cf_mitigated_считается()
    {
        // Этот заголовок Cloudflare ставит, когда сама вмешалась в запрос.
        Assert.True(IsChallenge(Response(HttpStatusCode.Forbidden, ("cf-mitigated", "challenge"))));
        Assert.True(IsChallenge(Response(HttpStatusCode.ServiceUnavailable, ("cf-mitigated", "challenge"))));
    }

    [Fact]
    public void Успешный_ответ_проверкой_не_считается()
    {
        Assert.False(IsChallenge(Response(HttpStatusCode.OK, ("cf-mitigated", "challenge"))));
        Assert.False(IsChallenge(null));
    }

    [Theory]
    [InlineData("<html><head><title>Just a moment...</title></head></html>")]
    [InlineData("<div class=\"cf-browser-verification\"></div>")]
    [InlineData("window._cf_chl_opt = {}")]
    [InlineData("/cdn-cgi/challenge-platform/h/b/orchestrate/chl_page/v1")]
    public void Разметка_задачи_в_теле_считается(string body)
    {
        // Старые виды проверки приходят без cf-mitigated, только телом.
        Assert.True(CloudflareClearance.IsChallengeBody(body));
    }

    [Theory]
    [InlineData("<html><body>Форум временно недоступен</body></html>")]
    [InlineData("504 Gateway Time-out")]
    [InlineData("")]
    [InlineData(null)]
    public void Обычная_страница_отказа_проверкой_не_считается(string body)
    {
        Assert.False(CloudflareClearance.IsChallengeBody(body));
    }

    [Fact]
    public void Большое_тело_не_разбираем()
    {
        // Страница выдачи весит сотни килобайт, и искать в ней разметку
        // задачи бессмысленно: проверка приходит короткой заглушкой.
        Assert.False(CloudflareClearance.IsChallengeBody(new string('a', 300_000) + "Just a moment"));
    }

    static bool IsChallenge(HttpResponseMessage r) => CloudflareClearance.IsChallenge(r);
}
