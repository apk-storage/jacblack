using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Indexers;
using JacBlack.Models.Api;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Год — признак слабый: карточка берёт его у TMDB, трекеры пишут свой, и они
/// расходятся сплошь и рядом. У «Обсессии» Лампа шлёт 2026, а все 44 раздачи
/// в базе помечены 2025 — строгий год оставлял ОДНУ раздачу из сорока четырёх.
/// Причём у выброшенных стоял тот же код IMDB, что у выжившей: мы точно знали,
/// что это тот же фильм, и всё равно его теряли.
///
/// Поэтому код сильнее года: совпал код с карточкой — год не спрашиваем.
/// Там, где кода нет, год остаётся условием, как и был.
/// </summary>
public class CodeBeatsYearTests
{
    const string Obsession = "tt37287335";
    const string Чужой = "tt99999999";

    static Result R(int year, string imdb, string title = null) => new()
    {
        Title = title ?? $"Обсессия / Obsession ({year}) WEB-DL 2160p",
        info = new TorrentInfo
        {
            name = "Обсессия",
            originalname = "Obsession",
            relased = year,
            types = new[] { "movie" },
            imdb = imdb,
        }
    };

    static IndexerSearchRequest Card() => new()
    {
        Title = "Обсессия",
        TitleOriginal = "Obsession",
        Year = 2026,          // TMDB датирует 2026, трекеры пишут 2025
        IsSerial = 1,
    };

    [Fact]
    public void Раздачи_чужого_года_с_кодом_карточки_остаются()
    {
        var выдача = new List<Result>
        {
            R(2026, Obsession),   // единственная с «правильным» годом — она и голосует за код
            R(2025, Obsession),
            R(2025, Obsession),
            R(2025, Obsession),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Card(), originalGiven: true, titleGiven: true);

        Assert.Equal(4, got.Count);
    }

    [Fact]
    public void Чужой_код_не_спасает_чужой_год()
    {
        // Поблажка только своим: раздача соседнего года с ДРУГИМ кодом — это
        // фильм-тёзка, и её по-прежнему выбрасывает заслон по коду.
        var выдача = new List<Result>
        {
            R(2026, Obsession),
            R(2025, Чужой),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Card(), originalGiven: true, titleGiven: true);

        Assert.Single(got);
        Assert.Equal(Obsession, got[0].info.imdb);
    }

    [Fact]
    public void Без_кода_год_остаётся_условием()
    {
        // Подтвердить нечем — работает прежнее правило, и раздача чужого года
        // выбывает. Иначе в карточку фильма вернулись бы тёзки соседних лет,
        // ради которых строгий год и вводился.
        var выдача = new List<Result>
        {
            R(2026, Obsession),
            R(2025, null),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Card(), originalGiven: true, titleGiven: true);

        Assert.Single(got);
        Assert.Equal(2026, got[0].info.relased);
    }

    [Fact]
    public void Когда_кода_нет_ни_у_кого_ничего_не_меняется()
    {
        var выдача = new List<Result> { R(2026, null), R(2025, null), R(2024, null) };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Card(), originalGiven: true, titleGiven: true);

        Assert.Single(got);
        Assert.Equal(2026, got[0].info.relased);
    }
}
