using System.Collections.Generic;
using JacBlack.Infrastructure.Indexers;
using JacBlack.Models.Api;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// У аниме-раздач года в названии не бывает вовсе: «[SubsPlease] Tsue to
/// Tsurugi no Wistoria S2 - 04 (1080p) [DC34B4A7].mkv». Взять его неоткуда —
/// ни animetosho, ни nyaa года не отдают, кода IMDB у них тоже нет.
///
/// А правило «человек указал год — раздача без года выбывает» отсекало их все
/// разом: по запросу из карточки с year=2024 не находилось НИЧЕГО, хотя четыре
/// раздачи лежали в базе. Без года в запросе те же четыре находились.
///
/// Поэтому послабление: раздача без года проходит, если карточка сама назвалась
/// сериалом (или аниме) И раздача несёт номер сезона либо серии. Для карточки
/// ФИЛЬМА правило остаётся строгим — там год-ноль и тащил в «Одиссею» 2026
/// чужие сериалы 1968, 1992 и 1994 годов.
/// </summary>
public class AnimeWithoutYearTests
{
    static Result Раздача(string title, int year = 0) => new()
    {
        Title = title,
        info = new TorrentInfo
        {
            name = "Tsue to Tsurugi no Wistoria",
            originalname = "Tsue to Tsurugi no Wistoria",
            relased = year,
            types = new[] { "anime" },
        }
    };

    static IndexerSearchRequest Карточка(int isSerial) => new()
    {
        Title = "Tsue to Tsurugi no Wistoria",
        TitleOriginal = "Tsue to Tsurugi no Wistoria",
        Year = 2024,
        IsSerial = isSerial,
    };

    [Fact]
    public void Серия_без_года_попадает_в_карточку_сериала()
    {
        var выдача = new List<Result>
        {
            Раздача("[SubsPlease] Tsue to Tsurugi no Wistoria S2 - 04 (1080p) [DC34B4A7].mkv"),
            Раздача("[ASW] Tsue to Tsurugi no Wistoria S2 - 04 [1080p HEVC x265 10Bit][AAC]"),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка(isSerial: 2), originalGiven: true, titleGiven: true);

        Assert.Equal(2, got.Count);
    }

    // Оба отрицательных случая проверяются вместе с «якорной» раздачей, у
    // которой год разобран и подходит. Без неё строгий отбор давал бы пусто, и
    // срабатывал последний рубеж — он отдаёт совпавшие по названию записи без
    // года, когда иначе выдача была бы пустой. Тест без якоря проверял бы его,
    // а не проверяемое правило.

    [Fact]
    public void В_карточку_фильма_раздача_без_года_по_прежнему_не_идёт()
    {
        // Ровно то, ради чего строгое правило вводилось: подтвердить год нечем,
        // а название у фильмов-тёзок совпадает сплошь и рядом.
        var выдача = new List<Result>
        {
            Раздача("Tsue to Tsurugi no Wistoria (2024) 1080p", year: 2024),
            Раздача("[SubsPlease] Tsue to Tsurugi no Wistoria S2 - 04 (1080p) [DC34B4A7].mkv"),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка(isSerial: 1), originalGiven: true, titleGiven: true);

        Assert.Single(got);
        Assert.Equal(2024, got[0].info.relased);
    }

    [Fact]
    public void Без_номера_серии_послабление_не_действует()
    {
        // Признак серийности — единственное, чем такая раздача отличается от
        // безымянного тёзки. Нет его — нет и поблажки, даже у карточки сериала.
        var выдача = new List<Result>
        {
            Раздача("Tsue to Tsurugi no Wistoria (2024) 1080p", year: 2024),
            Раздача("Tsue to Tsurugi no Wistoria [1080p][Multi-Subs]"),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка(isSerial: 2), originalGiven: true, titleGiven: true);

        Assert.Single(got);
        Assert.Equal(2024, got[0].info.relased);
    }

    [Fact]
    public void Раздача_с_разобранным_годом_судится_как_прежде()
    {
        // Послабление касается только тех, у кого года нет. Если год разобран,
        // работает обычное правило сериала: сезоны идут вперёд, назад — на год.
        var выдача = new List<Result>
        {
            Раздача("Tsue to Tsurugi no Wistoria S2 - 04 (2026)", year: 2026),
            Раздача("Tsue to Tsurugi no Wistoria S1 - 01 (2020)", year: 2020),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка(isSerial: 2), originalGiven: true, titleGiven: true);

        Assert.Single(got);
        Assert.Equal(2026, got[0].info.relased);
    }
}
