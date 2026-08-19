using JacBlack.Infrastructure.Indexers;
using JacBlack.Models.Api;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Сезон и серия, которые мы отдаём клиенту.
///
/// Разбор знал только «S01E05» и «1x05». У аниме таких обозначений не бывает
/// вовсе: номер серии пишут через тире — «[SubsPlease] Wistoria S2 - 04
/// (1080p)», — поэтому у всей аниме-выдачи серия оставалась неизвестной и
/// Лампа не могла разложить раздачи по сериям. На это пожаловались 19.08.2026.
///
/// Второе: диапазоны. «[01-12]» — это набор за сезон, и отдавать оттуда номер
/// одной серии нельзя, иначе раздача ляжет не туда.
/// </summary>
public class EpisodeRangeTests
{
    static (int? season, int? episode) Разобрать(string title) =>
        SeasonEpisodeFilter.AttrsFromResult(new Result { Title = title });

    [Theory]
    [InlineData("[SubsPlease] Tsue to Tsurugi no Wistoria S2 - 04 (1080p) [D8BE1].mkv", 2, 4)]
    [InlineData("[Erai-raws] Black Torch - 07 [1080p CR WEB-DL AVC AAC]", 1, 7)]
    [InlineData("[AsukaRaws] Hell Mode S2 - 07 (19) (WEB-DL 1280x720)", 2, 7)]
    [InlineData("[Group] Dandadan - 12v2 (1080p)", 1, 12)]
    public void Серия_аниме_разбирается(string title, int сезон, int серия)
    {
        var (s, e) = Разобрать(title);

        Assert.Equal(сезон, s);
        Assert.Equal(серия, e);
    }

    [Theory]
    [InlineData("[SubsPlease] Tsue to Tsurugi no Wistoria S2 [01-12] (1080p) [Batch]", 2)]
    [InlineData("Сериал / Series (2024) WEB-DL S01E01-E12", 1)]
    [InlineData("[Judas] Some Anime S03 [01~24] [1080p]", 3)]
    public void Диапазон_отдаётся_как_набор_за_сезон(string title, int сезон)
    {
        // Сезон известен, конкретная серия — нет. Ровно так клиент и должен
        // понять набор.
        var (s, e) = Разобрать(title);

        Assert.Equal(сезон, s);
        Assert.Null(e);
    }

    [Fact]
    public void Диапазон_в_русском_виде_тоже_набор()
    {
        var (s, e) = Разобрать("Дюна: Пророчество 1 сезон (1-6 из 6) / Dune: Prophecy (2024) WEB-DLRip S01");

        Assert.Equal(1, s);
        Assert.Null(e);
    }

    [Theory]
    [InlineData("Game of Thrones S02E08 The Prince of Winterfell 1080p", 2, 8)]
    [InlineData("Сериал 3x07 (2024) WEB-DL", 3, 7)]
    public void Привычные_обозначения_работают_как_прежде(string title, int сезон, int серия)
    {
        var (s, e) = Разобрать(title);

        Assert.Equal(сезон, s);
        Assert.Equal(серия, e);
    }

    [Theory]
    [InlineData("Some Movie (2024) 1080p BluRay x264-GROUP")]
    [InlineData("Дюна / Dune: Part One (2021) BDRip 1080p")]
    public void У_фильма_серии_не_выдумываются(string title)
    {
        var (s, e) = Разобрать(title);

        Assert.Null(s);
        Assert.Null(e);
    }

    [Fact]
    public void Число_в_названии_не_становится_серией()
    {
        // Тире с числом встречается и вне нумерации серий: «x264-R», «AAC5.1».
        // Номер засчитывается только перед скобкой, концом строки или
        // расширением файла.
        var (_, e) = Разобрать("Game of Thrones - Season 8 S08 - 2019 1080p Bluray AAC5.1 x264-R");

        Assert.Null(e);
    }
}
