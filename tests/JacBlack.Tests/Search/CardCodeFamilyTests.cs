using System.Collections.Generic;
using JacBlack.Infrastructure.Indexers;
using JacBlack.Models.Api;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Код IMDB у сериала не один. Сезоны и эпизоды сериала имеют СВОИ коды,
/// отличные от кода сериала: у «The Boys» сериал — tt1190634, а 3 сезон —
/// tt22298582. Строгий заслон «код не совпал — чужое» выкидывал ровно такие
/// раздачи (DV-релизы 3 сезона несут код сезона), хотя это тот же сериал; в
/// вебе они были видны (свободный поиск заслон не применяет), а в Лампе
/// пропадали. Замер 07.08.2026: 12 раздач с кодом сезона в свободном поиске,
/// 0 доходило до карточки.
///
/// Разделитель — подтверждённость: настоящий сезон несёт свой код на МНОГИХ
/// раздачах, случайный мистег чужого фильма — на одной. Для фильмов заслон
/// остаётся строгим (у фильма код один).
/// </summary>
public class CardCodeFamilyTests
{
    const string SeriesCode = "tt1190634";   // сам сериал The Boys
    const string SeasonCode = "tt22298582";  // 3 сезон The Boys
    const string AlienCode = "tt9999999";    // заведомо чужой фильм-тёзка

    static Result R(string name, string original, int year, string[] types, string imdb) => new()
    {
        Title = $"{name} / {original} / {year}",
        Seeders = 10,
        info = new TorrentInfo
        {
            name = name,
            originalname = original,
            relased = year,
            types = types,
            imdb = imdb,
        },
    };

    static IndexerSearchRequest Card() => new()
    {
        Title = "Пацаны",
        TitleOriginal = "The Boys",
        Year = 2019,     // Лампа шлёт год ПРЕМЬЕРЫ сериала, не сезона
        IsSerial = 2,
    };

    static readonly string[] Serial = { "serial" };

    [Fact]
    public void Раздачи_сезона_с_кодом_сезона_остаются_в_карточке_сериала()
    {
        // Сезон подтверждён множеством раздач — код сезона входит в семью.
        var выдача = new List<Result>
        {
            R("Пацаны", "The Boys", 2020, Serial, SeriesCode),   // 1-2 сезон, код сериала
            R("Пацаны", "The Boys", 2022, Serial, SeriesCode),
            R("Пацаны", "The Boys", 2022, Serial, SeasonCode),   // 3 сезон, код сезона
            R("Пацаны", "The Boys", 2022, Serial, SeasonCode),   // ещё раздача 3 сезона (подтверждение)
        };

        var got = IndexerSearchHelper.FilterByCardTitle(выдача, Card(), originalGiven: true, titleGiven: true);

        Assert.Equal(4, got.Count);
        Assert.Contains(got, r => r.info.imdb == SeasonCode);
    }

    [Fact]
    public void Одинокий_чужой_код_всё_ещё_выкидывается()
    {
        // Тёзка на одной раздаче в семью не попадает — заслон её режет.
        var выдача = new List<Result>
        {
            R("Пацаны", "The Boys", 2020, Serial, SeriesCode),
            R("Пацаны", "The Boys", 2022, Serial, SeriesCode),
            R("Пацаны", "The Boys", 2022, Serial, AlienCode),    // одиночка — чужое
        };

        var got = IndexerSearchHelper.FilterByCardTitle(выдача, Card(), originalGiven: true, titleGiven: true);

        Assert.DoesNotContain(got, r => r.info.imdb == AlienCode);
        Assert.Equal(2, got.Count);
    }

    [Fact]
    public void У_фильма_несовпавший_код_режется_даже_при_повторе()
    {
        // Для фильма семьи нет: два разных фильма-тёзки одного года — это две
        // разные вещи, и код обязан совпасть. Даже двукратный чужой код не
        // должен пройти (иначе вернулась бы тёзка «Одиссеи»).
        var card = new IndexerSearchRequest
        {
            Title = "Одиссея",
            TitleOriginal = "The Odyssey",
            Year = 2026,
            IsSerial = 0,
        };
        string[] movie = { "movie" };

        var выдача = new List<Result>
        {
            R("Одиссея", "The Odyssey", 2026, movie, "tt33764258"),   // настоящий, большинство
            R("Одиссея", "The Odyssey", 2026, movie, "tt33764258"),
            R("Одиссея", "The Odyssey", 2026, movie, "tt33764258"),
            R("Одиссея", "The Odyssey", 2026, movie, "tt41605854"),   // тёзка, даже дважды
            R("Одиссея", "The Odyssey", 2026, movie, "tt41605854"),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(выдача, card, originalGiven: true, titleGiven: true);

        Assert.All(got, r => Assert.Equal("tt33764258", r.info.imdb));
        Assert.Equal(3, got.Count);
    }
}
