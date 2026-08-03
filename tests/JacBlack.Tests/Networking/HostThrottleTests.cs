using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Networking;
using Xunit;

namespace JacBlack.Tests.Networking;

/// <summary>
/// Реакция на просьбу трекера сбавить обороты.
///
/// До 31.07.2026 обработки 429 не было вовсе: ответ считался обычной
/// неудачей, страница пропускалась, и обход продолжал стучаться с той же
/// частотой. Банят обычно не за скорость саму по себе, а за то, что на
/// явную просьбу подождать никто не отвечает.
/// </summary>
public class HostThrottleTests
{
    static HttpResponseMessage TooMany(RetryConditionHeaderValue retryAfter = null)
    {
        var r = new HttpResponseMessage((HttpStatusCode)429);
        if (retryAfter != null)
            r.Headers.RetryAfter = retryAfter;
        return r;
    }

    [Fact]
    public void Опознаёт_просьбу_подождать()
    {
        Assert.True(HostThrottle.IsThrottleResponse(TooMany()));
        Assert.False(HostThrottle.IsThrottleResponse(new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.False(HostThrottle.IsThrottleResponse(new HttpResponseMessage(HttpStatusCode.Forbidden)));
        Assert.False(HostThrottle.IsThrottleResponse(null));
    }

    [Fact]
    public void Берёт_паузу_из_заголовка_в_секундах()
    {
        const string host = "throttle-seconds.test";
        HostThrottle.Reset(host);

        HostThrottle.Throttled(host, TooMany(new RetryConditionHeaderValue(TimeSpan.FromSeconds(45))));

        var left = HostThrottle.Remaining(host);
        Assert.InRange(left.TotalSeconds, 40, 46);
    }

    [Fact]
    public void Берёт_паузу_из_заголовка_с_датой()
    {
        // Retry-After разрешает обе формы, и трекеры пользуются обеими.
        const string host = "throttle-date.test";
        HostThrottle.Reset(host);

        HostThrottle.Throttled(host, TooMany(new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(30))));

        Assert.InRange(HostThrottle.Remaining(host).TotalSeconds, 20, 31);
    }

    [Fact]
    public void Без_заголовка_пауза_нарастает()
    {
        const string host = "throttle-growing.test";
        HostThrottle.Reset(host);

        HostThrottle.Throttled(host, TooMany());
        double first = HostThrottle.Remaining(host).TotalSeconds;

        HostThrottle.Throttled(host, TooMany());
        double second = HostThrottle.Remaining(host).TotalSeconds;

        Assert.True(second > first, $"вторая пауза {second:F0} с не длиннее первой {first:F0} с");
    }

    [Fact]
    public void Пауза_имеет_потолок()
    {
        // Иначе одна упрямая полоса отказов выключила бы трекер на полдня,
        // тогда как глубокий обход всё равно закончится по своему сроку.
        const string host = "throttle-cap.test";
        HostThrottle.Reset(host);

        for (int i = 0; i < 12; i++)
            HostThrottle.Throttled(host, TooMany());

        Assert.InRange(HostThrottle.Remaining(host).TotalMinutes, 0, 10.1);
    }

    [Fact]
    public void Удачный_ответ_сбрасывает_счётчик()
    {
        const string host = "throttle-recover.test";
        HostThrottle.Reset(host);

        HostThrottle.Throttled(host, TooMany());
        HostThrottle.Throttled(host, TooMany());
        HostThrottle.Ok(host);
        HostThrottle.Reset(host);
        HostThrottle.Throttled(host, TooMany());

        // После сброса пауза снова начальная, а не накопленная.
        Assert.InRange(HostThrottle.Remaining(host).TotalSeconds, 10, 20);
    }

    [Fact]
    public void Пауза_касается_только_своего_хоста()
    {
        const string busy = "throttle-busy.test";
        const string calm = "throttle-calm.test";
        HostThrottle.Reset(busy);
        HostThrottle.Reset(calm);

        HostThrottle.Throttled(busy, TooMany(new RetryConditionHeaderValue(TimeSpan.FromSeconds(60))));

        Assert.True(HostThrottle.Remaining(busy) > TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, HostThrottle.Remaining(calm));
    }

    [Fact]
    public async Task Ждать_дольше_отпущенного_не_станем()
    {
        // Висеть до собственного таймаута бессмысленно: честнее пропустить
        // страницу и вернуться к ней следующим проходом.
        const string host = "throttle-budget.test";
        HostThrottle.Reset(host);

        HostThrottle.Throttled(host, TooMany(new RetryConditionHeaderValue(TimeSpan.FromMinutes(5))));

        Assert.False(await HostThrottle.WaitAsync(host, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Спокойный_хост_проходит_без_ожидания()
    {
        const string host = "throttle-free.test";
        HostThrottle.Reset(host);

        Assert.True(await HostThrottle.WaitAsync(host, TimeSpan.FromSeconds(1)));
    }
}
