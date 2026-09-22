using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Trackers;
using Xunit;

namespace JacBlack.Tests.Trackers;

/// <summary>
/// Второй заход по страницам, не давшимся с первого раза.
///
/// У rutracker в каждом круге терялось около 2 380 страниц из 14 827, причём
/// пачками: 477 неудач подряд при 500 пройденных. Это не мёртвые разделы, а
/// отрезки с отвалившимся браузером — поэтому в конце круга их надо пройти
/// ещё раз. Здесь проверяется, что повтор берётся ровно за неудачные, что
/// подобранное попадает в счётчики и что при развале входа второго захода
/// не будет вовсе.
/// </summary>
public class TrackerQueueProgressRetryTests
{
    static TrackerQueueProgress Обход(int total) =>
        new TrackerQueueProgress("тест", () => { }, total);

    public TrackerQueueProgressRetryTests()
    {
        // В бою пауза нужна, чтобы браузер успел подняться; в тесте она
        // превратила бы минуту ожидания в минуту простоя.
        TrackerQueueProgress.RetryPause = TimeSpan.Zero;
    }

    [Fact]
    public async Task Повтор_идёт_только_за_неудачными()
    {
        // Круг берём заведомо больше числа неудач: иначе сработает
        // предохранитель, отменяющий второй заход при развале входа.
        var обход = Обход(total: 10);
        var повторены = new List<int>();

        обход.PageDone(true, async () => { повторены.Add(1); return await Task.FromResult(true); });
        обход.PageDone(false, async () => { повторены.Add(2); return await Task.FromResult(true); });
        обход.PageDone(false, async () => { повторены.Add(3); return await Task.FromResult(true); });

        Assert.Equal(2, обход.Pending);

        await обход.RetryFailedAsync();

        Assert.Equal(new[] { 2, 3 }, повторены);
    }

    [Fact]
    public async Task Подобранная_страница_переходит_из_неудач_в_разобранные()
    {
        var обход = Обход(total: 10);

        обход.PageDone(true);
        обход.PageDone(false, () => Task.FromResult(true));
        обход.PageDone(false, () => Task.FromResult(false));

        Assert.Equal(1, обход.Done);
        Assert.Equal(2, обход.Failed);

        await обход.RetryFailedAsync();

        Assert.Equal(1, обход.Recovered);
        Assert.Equal(2, обход.Done);
        Assert.Equal(1, обход.Failed);
    }

    [Fact]
    public async Task Развалившийся_вход_второго_захода_не_получает()
    {
        // 12.09.2026 у rutracker вышло «разобрано 0, не вышло 14 827»: беда
        // была не в страницах, а во входе. Повтор там лишь удвоил бы время.
        var обход = Обход(total: 4);
        bool ходили = false;

        for (int i = 0; i < 3; i++)
            обход.PageDone(false, () => { ходили = true; return Task.FromResult(true); });

        await обход.RetryFailedAsync();

        Assert.False(ходили);
        Assert.Equal(0, обход.Recovered);
        Assert.Equal(3, обход.Failed);
        Assert.Equal(0, обход.Pending);
    }

    [Fact]
    public async Task Упавший_повтор_не_валит_весь_заход()
    {
        var обход = Обход(total: 10);
        bool дошли_до_второй = false;

        обход.PageDone(true);
        обход.PageDone(false, () => throw new InvalidOperationException("браузер отвалился"));
        обход.PageDone(false, () => { дошли_до_второй = true; return Task.FromResult(true); });

        await обход.RetryFailedAsync();

        Assert.True(дошли_до_второй);
        Assert.Equal(1, обход.Recovered);
        Assert.Equal(1, обход.Failed);
    }

    [Fact]
    public async Task Второй_заход_обрывается_по_времени()
    {
        // 22.09.2026 второй заход шёл 48 минут и подобрал одну страницу из
        // 2 379. Столько браузерного времени за страницу не стоит: остальные
        // и так попадут в следующий круг, поэтому есть потолок.
        var прежний = TrackerQueueProgress.RetryBudget;
        TrackerQueueProgress.RetryBudget = TimeSpan.Zero;
        try
        {
            var обход = Обход(total: 100);
            int пройдено = 0;

            for (int i = 0; i < 5; i++)
                обход.PageDone(false, async () => { пройдено++; return await Task.FromResult(false); });

            await обход.RetryFailedAsync();

            // Потолок нулевой: первая же проверка времени обрывает заход,
            // поэтому пройти успевает только одна страница из пяти.
            Assert.Equal(1, пройдено);
        }
        finally
        {
            TrackerQueueProgress.RetryBudget = прежний;
        }
    }

    [Fact]
    public async Task Без_повтора_счётчики_прежние()
    {
        // Трекеру без потерь (megapeer разбирает все 77 страниц круга)
        // повтор передавать незачем, и поведение у него должно остаться тем же.
        var обход = Обход(total: 2);

        обход.PageDone(true);
        обход.PageDone(false);

        await обход.RetryFailedAsync();

        Assert.Equal(1, обход.Done);
        Assert.Equal(1, обход.Failed);
        Assert.Equal(0, обход.Recovered);
    }
}
