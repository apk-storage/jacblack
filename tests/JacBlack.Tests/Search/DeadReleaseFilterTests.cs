using System.Collections.Generic;
using JacBlack.Application.Search;
using JacBlack.Models.Api;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Отсев заведомо мёртвых раздач из выдачи.
///
/// Мёртвой считается раздача, у которой совпали ДВА признака: обход живости
/// видел ноль раздающих подряд не меньше порога И живой опрос при поиске тоже
/// дал ноль. Одного счётчика мало — опрос воскрешает раздачи постоянно.
///
/// Главное, что здесь закреплено, — гарантия «никогда не оставлять пусто».
/// Замер 06.08.2026: порог набрали 39% раздач базы, и у 26% карточек живых
/// копий нет вовсе. Безоглядный отсев опустошил бы каждую четвёртую.
/// </summary>
public class DeadReleaseFilterTests
{
    static Result R(int deadChecks, int seeders, string title = "раздача") => new()
    {
        Title = title,
        Seeders = seeders,
        info = new TorrentInfo { deadChecks = deadChecks, name = title }
    };

    [Fact]
    public void Мёртвые_убираются_когда_есть_живые()
    {
        var выдача = new List<Result>
        {
            R(deadChecks: 0, seeders: 12, title: "живая"),
            R(deadChecks: 9, seeders: 0, title: "мёртвая"),
        };

        var got = JackettSearchService.DropDeadUnlessNothingLeft(выдача);

        Assert.Single(got);
        Assert.Equal("живая", got[0].Title);
    }

    [Fact]
    public void Если_живых_нет_отдаём_мёртвые_как_есть()
    {
        var выдача = new List<Result>
        {
            R(deadChecks: 7, seeders: 0, title: "первая"),
            R(deadChecks: 9, seeders: 0, title: "вторая"),
        };

        var got = JackettSearchService.DropDeadUnlessNothingLeft(выдача);

        Assert.Equal(2, got.Count);
    }

    /// <summary>
    /// Счётчик нулей — это прошлое, а раздающие сейчас — настоящее. Раздача,
    /// которую воскресил живой опрос, обязана остаться, сколько бы нулей за
    /// ней ни числилось.
    /// </summary>
    [Fact]
    public void Воскресшая_остаётся_несмотря_на_счётчик()
    {
        var выдача = new List<Result>
        {
            R(deadChecks: 20, seeders: 3, title: "воскресла"),
            R(deadChecks: 20, seeders: 0, title: "мёртвая"),
        };

        var got = JackettSearchService.DropDeadUnlessNothingLeft(выдача);

        Assert.Single(got);
        Assert.Equal("воскресла", got[0].Title);
    }

    /// <summary>
    /// Ноль раздающих сам по себе не приговор: у раздачи, которую сторож ещё
    /// не проверял, счётчик пуст, и выбрасывать её не за что.
    /// </summary>
    [Fact]
    public void Непроверенная_с_нулём_не_считается_мёртвой()
    {
        var выдача = new List<Result>
        {
            R(deadChecks: 0, seeders: 0, title: "непроверенная"),
            R(deadChecks: 0, seeders: 5, title: "живая"),
        };

        var got = JackettSearchService.DropDeadUnlessNothingLeft(выдача);

        Assert.Equal(2, got.Count);
    }

    [Fact]
    public void Пустая_и_нулевая_выдача_не_ломают_правило()
    {
        Assert.Empty(JackettSearchService.DropDeadUnlessNothingLeft(new List<Result>()));
        Assert.Null(JackettSearchService.DropDeadUnlessNothingLeft(null));
    }
}
