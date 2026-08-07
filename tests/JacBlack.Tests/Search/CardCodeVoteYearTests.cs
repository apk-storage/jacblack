using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Indexers;
using JacBlack.Models.Api;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Код карточки выводится голосованием по её же выдаче — словарь знает не всё.
/// Но голосовать может только раздача, чей ГОД подходит карточке, иначе
/// побеждает более многочисленный тёзка соседних лет и объявляет своим чужой
/// код. Дальше заслон «код не совпал — чужое» выкашивает как раз верные
/// раздачи, а уцелевают записи вообще без кода.
///
/// Замер на живом проде 07.08.2026, карточка «Дюна / Dune» 2021: выдача упала
/// со 125 раздач до 22, и все 22 оказались без кода. Виноваты были раздачи
/// «Дюна: Часть вторая» 2024 года — их вдвое больше, и голос забрали они.
/// </summary>
public class CardCodeVoteYearTests
{
    const string DunePartOne = "409424";   // Дюна, 2021 — то, что просит карточка
    const string DunePartTwo = "4540126";  // Дюна: Часть вторая, 2024 — тёзка

    static Result R(int year, string kinopoisk) => new()
    {
        Title = $"Дюна / Dune ({year})",
        info = new TorrentInfo
        {
            name = "Дюна",
            originalname = "Dune",
            relased = year,
            types = new[] { "movie" },
            kinopoisk = kinopoisk
        }
    };

    static List<Result> Выдача()
    {
        var list = new List<Result>();

        // Тёзка многочисленнее — как на самом трекере.
        for (int i = 0; i < 12; i++)
            list.Add(R(2024, DunePartTwo));

        for (int i = 0; i < 5; i++)
            list.Add(R(2021, DunePartOne));

        // И записи без кода: раньше выживали только они.
        for (int i = 0; i < 3; i++)
            list.Add(R(2021, null));

        return list;
    }

    [Fact]
    public void Раздачи_чужого_года_код_карточки_не_выбирают()
    {
        var card = new IndexerSearchRequest
        {
            Title = "Дюна",
            TitleOriginal = "Dune",
            Year = 2021,
            IsSerial = 1,
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            Выдача(), card, originalGiven: true, titleGiven: true);

        // Все пять раздач 2021 года с верным кодом обязаны уцелеть, плюс три
        // без кода. Раздачи 2024 года выбывают по году, а не по коду.
        Assert.Equal(8, got.Count);
        Assert.Equal(5, got.Count(r => r.info.kinopoisk == DunePartOne));
        Assert.DoesNotContain(got, r => r.info.kinopoisk == DunePartTwo);
    }
}
