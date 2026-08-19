using System.Collections.Generic;
using JacBlack.Infrastructure.Indexers;
using JacBlack.Models.Api;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// У аниме Лампа шлёт русское название и японское иероглифами — «Адский
/// режим» и «ヘルモード…», — а англоязычные трекеры подписывают раздачи ромадзи:
/// «Hell Mode: Yarikomizuki no Gamer…». Общего в этих строках нет ни буквы,
/// поэтому nyaa и knaben выпадали из карточки целиком.
///
/// Замер 19.08.2026: по «Адскому режиму» находилось 22 раздачи и ни одной
/// nyaa; по «Hell Mode» — 109, из них 60 nyaa.
///
/// Ромадзи знают наши же русские аниме-трекеры: у aniliberty, animelayer и
/// bitru в записи лежат оба имени. Заслон обязан принимать совпадение по нему,
/// иначе второй заход бесполезен — найденное тут же выбросится.
/// </summary>
public class RomajiPickupTests
{
    const string Японское = "ヘルモード ～やり込み好きのゲーマーは廃設定の異世界で無双する～";
    const string Ромадзи = "Hell Mode: Yarikomizuki no Gamer wa Haisettei no Isekai de Musou Suru";

    static Result Раздача(string имя, string оригинал, int year = 2026) => new()
    {
        Title = оригинал + " - 07 [1080p]",
        info = new TorrentInfo
        {
            name = имя,
            originalname = оригинал,
            relased = year,
            types = new[] { "anime" },
        }
    };

    static IndexerSearchRequest Карточка(string romaji = null) => new()
    {
        Title = "Адский режим",
        TitleOriginal = Японское,
        TitleRomaji = romaji,
        Year = 2026,
        IsSerial = 2,
    };

    [Fact]
    public void Раздача_с_ромадзи_проходит_когда_имя_подхвачено()
    {
        var выдача = new List<Result> { Раздача("Hell Mode", Ромадзи) };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка(Ромадзи), originalGiven: true, titleGiven: true);

        Assert.Single(got);
    }

    [Fact]
    public void Без_подхваченного_имени_совпасть_нечем()
    {
        // Это состояние ДО второго захода: карточка знает только японское имя,
        // и заслон честно не находит совпадения. Тест закрепляет причину, по
        // которой понадобился подхват, — чтобы её не «починили» ослаблением
        // заслона.
        var выдача = new List<Result> { Раздача("Hell Mode", Ромадзи) };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка(romaji: null), originalGiven: true, titleGiven: true);

        Assert.Empty(got);
    }

    [Fact]
    public void Чужое_аниме_ромадзи_не_протаскивает()
    {
        // Подхват расширяет список допустимых имён, а не отменяет проверку:
        // другое название остаётся другим.
        var выдача = new List<Result> { Раздача("Frieren", "Sousou no Frieren") };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка(Ромадзи), originalGiven: true, titleGiven: true);

        Assert.Empty(got);
    }

    [Fact]
    public void Короткое_имя_у_англоязычного_трекера_совпадает_с_базовым()
    {
        // nyaa подписывает эти же раздачи «Hell Mode S2», без подзаголовка.
        // Подхват берёт базовую часть имени, поэтому они должны проходить.
        var выдача = new List<Result> { Раздача("Hell Mode S2", "Hell Mode S2") };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка("Hell Mode"), originalGiven: true, titleGiven: true);

        Assert.Single(got);
    }

    [Fact]
    public void Русское_название_по_прежнему_работает()
    {
        // Записи русских трекеров, через которые ромадзи и добывается, из
        // выдачи выпасть не должны.
        var выдача = new List<Result> { Раздача("Адский режим", Ромадзи) };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка(Ромадзи), originalGiven: true, titleGiven: true);

        Assert.Single(got);
    }
}
