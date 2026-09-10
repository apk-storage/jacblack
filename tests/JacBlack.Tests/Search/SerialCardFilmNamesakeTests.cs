using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Indexers;
using JacBlack.Models.Api;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// В карточку сериала не должен попадать одноимённый фильм других лет.
///
/// Замер 10.09.2026 на запросе с телевизора («Каратель» / «Marvel's The
/// Punisher», 2017, is_serial=2): из 83 раздач 49 были фильмом «Каратель:
/// Последнее убийство» 2026 года. Название совпадало по правилу подзаголовка,
/// тип у раздачи фильмовый, но допуск по году брался сериальный — «не раньше
/// 2016», и 2026 в него укладывался. Человек искал первый сезон, а получал
/// чужой фильм вперемешку с сезонами.
/// </summary>
public class SerialCardFilmNamesakeTests
{
    static Result Раздача(string title, int year, string[] types, string name = "Каратель") => new()
    {
        Title = title,
        info = new TorrentInfo
        {
            name = name,
            originalname = "The Punisher",
            relased = year,
            types = types,
        }
    };

    static IndexerSearchRequest Карточка => new()
    {
        Title = "Каратель",
        TitleOriginal = "The Punisher",
        Year = 2017,
        IsSerial = 2,
    };

    [Fact]
    public void Фильм_тёзка_других_лет_в_карточку_сериала_не_попадает()
    {
        var выдача = new List<Result>
        {
            Раздача("Каратель / The Punisher [S01] (2017) WEBRip-AVC", 2017, new[] { "serial" }),
            Раздача("Каратель / The Punisher [S02] (2019) WEBRip 1080p", 2019, new[] { "serial" }),
            Раздача("Каратель: Последнее убийство / The Punisher: One Last Kill (2026) WEB-DL",
                2026, new[] { "movie" }, name: "Каратель: Последнее убийство"),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка, originalGiven: true, titleGiven: true);

        Assert.Equal(2, got.Count);
        Assert.DoesNotContain(got, r => r.Title.Contains("One Last Kill"));
    }

    /// <summary>
    /// Сборник сезонов трекеры иногда кладут с фильмовым типом. Пометка сезона
    /// в названии — признак, что это всё-таки сериал, и терять его нельзя.
    /// </summary>
    [Fact]
    public void Сборник_сезонов_с_фильмовым_типом_остаётся()
    {
        var выдача = new List<Result>
        {
            Раздача("Каратель / The Punisher (1-2 сезон: 1-26 серии из 26) (2019)", 2019, new[] { "movie" }),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка, originalGiven: true, titleGiven: true);

        Assert.Single(got);
    }

    /// <summary>
    /// Фильм того же года, что и карточка, остаётся: это чаще всего сама
    /// раздача сериала, у которой тип разобрался фильмовым.
    /// </summary>
    [Fact]
    public void Фильмовый_тип_того_же_года_остаётся()
    {
        var выдача = new List<Result>
        {
            Раздача("Каратель / The Punisher (2017) WEB-DL", 2017, new[] { "movie" }),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка, originalGiven: true, titleGiven: true);

        Assert.Single(got);
    }
}
