using System.Collections.Generic;
using System.Linq;
using JacBlack.Application.Search;
using JacBlack.Models.Api;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Порядок выдачи: раздачи с русской дорожкой впереди англоязычных.
///
/// Повод (Сергей, 09.10.2026): «почему я в Лампе Кэрри от рудаб так и не
/// увидел?» Раздача была — на 63-м месте из 66. Сортировка шла по сидам, а у
/// англоязычных раздач «Carrie» их 600–1300 против 3–37 у четырёх русских с
/// rudub, и те оказывались за концом списка. Выдача формально верна, а для
/// человека пуста: jac.black ищет для русскоязычной Лампы.
///
/// Зарубежные источники при этом не выбрасываются — они заведены ради донора
/// озвучек Перегона, и раздача с русским звуком проходит первым ключом даже с
/// knaben: признак берётся из названия (кириллица или пометка rus), а не из
/// имени трекера.
/// </summary>
public class RussianFirstOrderTests
{
    static Result R(string tracker, string title, int seeders, bool live = false) => new()
    {
        Tracker = tracker,
        Title = title,
        Seeders = seeders,
        info = new TorrentInfo { name = title, seedersLive = live }
    };

    [Fact]
    public void Русская_раздача_впереди_англоязычной_с_тысячей_сидов()
    {
        var было = new List<Result>
        {
            R("eztv", "Carrie S01E01 1080p WEB H264-CAKES EZTV", 1287),
            R("knaben", "Carrie S01 (2026) 1080p WEBRip x265 | 1337x", 1023),
            R("rudub", "Кэрри (Carrie)Сезон 1 Серии 01-08 (HD1080p WEBRip)", 20)
        };

        var стало = JackettSearchService.Упорядочить(было);

        Assert.Equal("rudub", стало[0].Tracker);
    }

    [Fact]
    public void Среди_русских_порядок_по_сидам_сохраняется()
    {
        var было = new List<Result>
        {
            R("rudub", "Кэрри (Carrie) Серии 01-08 (HD720p WEBRip)", 7),
            R("rutracker", "Кэрри / Carrie [2026, WEB-DL 1080p]", 44),
            R("rudub", "Кэрри (Carrie) Серии 01-08 (WEBRip XviD)", 37)
        };

        var стало = JackettSearchService.Упорядочить(было);

        Assert.Equal(new[] { 44, 37, 7 }, стало.Select(r => r.Seeders).ToArray());
    }

    [Fact]
    public void Проверенные_сиды_всё_ещё_выше_непроверенных()
    {
        // Ключ, из-за которого удалённая раздача «Кода 8» оказывалась первой
        // строкой (02.08.2026). Новый ключ не должен его отменить: оба
        // кандидата здесь русские, значит спор решает именно проверенность.
        var было = new List<Result>
        {
            R("rutracker", "Код 8 / Code 8 [2019, BDRip]", 96),
            R("kinozal", "Код 8 / Code 8 (2019) WEB-DL", 44, live: true)
        };

        var стало = JackettSearchService.Упорядочить(было);

        Assert.Equal(44, стало[0].Seeders);
    }

    [Fact]
    public void Зарубежный_трекер_с_русским_звуком_не_уходит_в_конец()
    {
        var было = new List<Result>
        {
            R("eztv", "Carrie S01E03 1080p WEB H264", 900),
            R("knaben", "Carrie S01 2026 1080p WEBRip Rus Eng", 12)
        };

        var стало = JackettSearchService.Упорядочить(было);

        Assert.Equal(12, стало[0].Seeders);
    }

    [Fact]
    public void Пустой_список_и_пустые_поля_не_роняют_порядок()
    {
        Assert.Empty(JackettSearchService.Упорядочить(null));
        Assert.Empty(JackettSearchService.Упорядочить(new List<Result>()));

        var один = new List<Result> { new() { Tracker = null, Title = null } };
        Assert.Single(JackettSearchService.Упорядочить(один));
    }
}
