using JacRed.Infrastructure.Trackers.Eztv;
using Xunit;

namespace JacRed.Tests.Eztv;

/// <summary>
/// Сторож глубины обхода.
///
/// У EZTV листание упирается в потолок примерно на сотой странице: дальше API
/// молча возвращает один и тот же кусок ленты. Замерено 30.07.2026 — страницы
/// 100, 105 и 150 отдали идентичный набор идентификаторов. Обход, не знавший об
/// этом, прошёл 884 страницы и не добавил ни одной новой записи.
///
/// Отпечаток страницы позволяет заметить повтор и остановиться.
/// </summary>
public class EztvPageMarkTests
{
    static EztvItem[] Page(params long[] ids)
    {
        var items = new EztvItem[ids.Length];
        for (int i = 0; i < ids.Length; i++)
            items[i] = new EztvItem { Id = ids[i] };

        return items;
    }

    [Fact]
    public void Одинаковые_страницы_дают_одинаковый_отпечаток()
    {
        Assert.Equal(
            EztvSyncService.PageMark(Page(3116504, 3120000, 3123729)),
            EztvSyncService.PageMark(Page(3116504, 3120000, 3123729)));
    }

    [Fact]
    public void Разные_страницы_различаются()
    {
        // Соседние страницы ленты: границы окна не совпадают.
        Assert.NotEqual(
            EztvSyncService.PageMark(Page(3117194, 3122964)),
            EztvSyncService.PageMark(Page(3116613, 3130401)));
    }

    [Fact]
    public void Число_записей_входит_в_отпечаток()
    {
        // Границы те же, а середина усохла — это уже другая страница.
        Assert.NotEqual(
            EztvSyncService.PageMark(Page(100, 200, 300)),
            EztvSyncService.PageMark(Page(100, 300)));
    }

    [Fact]
    public void Пустая_страница_отпечатка_не_имеет()
    {
        // Иначе две пустые страницы подряд выглядели бы повтором, тогда как их
        // обрабатывает отдельная ветка.
        Assert.Null(EztvSyncService.PageMark(null));
        Assert.Null(EztvSyncService.PageMark(Page()));
    }
}
