using JacBlack.Infrastructure.Persistence;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Словарь кодов терял записи дважды за 07.08.2026 — сперва 444, потом ещё 475
/// уже ПОСЛЕ первой починки. Оба раза по одной схеме: словарь начинал
/// пополняться, не будучи прочитанным, и сохранение записывало горстку свежих
/// кодов поверх сотен накопленных. Атомарная запись от этого не спасает —
/// снимок был честным, просто пустым.
/// </summary>
public class IndexWriteGuardTests
{
    [Fact]
    public void Без_загрузки_писать_нельзя()
    {
        // Дыра, из-за которой заслон протёк в первый раз: «на диске
        // неизвестно» считалось разрешением.
        var guard = new IndexWriteGuard("тест");

        Assert.False(guard.MayWrite(10));
    }

    [Fact]
    public void После_неудачной_загрузки_писать_нельзя()
    {
        var guard = new IndexWriteGuard("тест");
        guard.LoadFailed("файл не прочитался");

        Assert.False(guard.MayWrite(1));
    }

    [Fact]
    public void Снимок_меньше_лежащего_на_диске_не_пишем()
    {
        var guard = new IndexWriteGuard("тест");
        guard.LoadSucceeded(444);

        Assert.False(guard.MayWrite(1));
        Assert.False(guard.MayWrite(443));
    }

    [Fact]
    public void Столько_же_или_больше_пишем()
    {
        var guard = new IndexWriteGuard("тест");
        guard.LoadSucceeded(444);

        Assert.True(guard.MayWrite(444));
        Assert.True(guard.MayWrite(500));
    }

    [Fact]
    public void Отсутствующий_файл_это_законный_пустой_старт()
    {
        // Свежая установка: писать можно с первой же записи.
        var guard = new IndexWriteGuard("тест");
        guard.FileMissing();

        Assert.True(guard.MayWrite(1));
    }

    [Fact]
    public void После_записи_планка_поднимается()
    {
        var guard = new IndexWriteGuard("тест");
        guard.FileMissing();
        guard.WriteSucceeded(120);

        Assert.False(guard.MayWrite(119));
        Assert.True(guard.MayWrite(120));
    }
}
