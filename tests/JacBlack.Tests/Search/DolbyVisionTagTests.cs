using JacBlack.Infrastructure.Parsing;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Dolby Vision в выдаче не различался никак: videotype знает только «sdr» и
/// «hdr», и DV-раздача была неотличима от обычной. Разница не косметическая —
/// профиль решает, покажет ли устройство верные цвета.
///
/// Два значения, ровно столько различает Лампа: «TV» — отдельно, всё прочее —
/// просто DV. Написания взяты живые, из базы на 07.08.2026.
/// </summary>
public class DolbyVisionTagTests
{
    [Theory]
    [InlineData("Пацаны / The Boys [S03] (2022) UHD WEB-DL-HEVC 2160p | 4K | HDR | Dolby Vision Profile 8 | D, P, P2")]
    [InlineData("Пацаны 5 сезон (1-8 из 8) / The Boys (2026) WEB-DL | 4К, HDR, HDR10+, Dolby Vision P8")]
    [InlineData("Пацаны / The Boys (2022) UHD WEB-DLRip [AV1/2160p] [4K, HDR10, DV Profile 10.1, 10-bit]")]
    [InlineData("Пацаны / The Boys (2026) WEB-DL [H.265/2160p] [4K, HDR10+, DV 8.1, 10-bit]")]
    [InlineData("Что-то там (2024) 2160p DoVi HDR10")]
    public void Опознаёт_обычный_DV(string title)
    {
        Assert.Equal(DolbyVisionKind.Dv, DolbyVisionTag.Detect(title));
        Assert.Equal("dv", DolbyVisionTag.Value(title));
    }

    [Theory]
    [InlineData("Пацаны (2 сезон: 1-8 серии из 8) / The Boys / 2020 / 4K, HEVC, HDR, HDR10+, Dolby Vision TV / Hybrid (2160p)")]
    [InlineData("Парни в лодке / The Boys in the Boat / 2023 / ПМ / 4K, HDR, Dolby Vision TV / Hybrid (2160p)")]
    [InlineData("Фильм (2022) 2160p DV TV")]
    public void Опознаёт_DV_TV_отдельно(string title)
    {
        Assert.Equal(DolbyVisionKind.DvTv, DolbyVisionTag.Detect(title));
        Assert.Equal("dvtv", DolbyVisionTag.Value(title));
    }

    [Theory]
    [InlineData("Пацаны (3 сезон) / The Boys / 2022 / 4K, HEVC, HDR, HDR10+ / WEB-DL (2160p)")]
    [InlineData("Реальные пацаны (1-3 сезоны) / 2010-2011 / РУ / DVDRip")]
    [InlineData("Сериал [03x15-16] (2011) DVB by kamyshin")]
    [InlineData("Фильм (2021) BDRip 1080p")]
    public void Не_видит_DV_там_где_его_нет(string title)
    {
        Assert.Equal(DolbyVisionKind.None, DolbyVisionTag.Detect(title));
        Assert.Null(DolbyVisionTag.Value(title));
    }

    [Fact]
    public void Голое_DV_без_профиля_признаком_не_считается()
    {
        // На трекерах «DV» встречается в перечислении озвучек, и по нему легко
        // приписать Dolby Vision раздаче, где его нет.
        Assert.Equal(DolbyVisionKind.None,
            DolbyVisionTag.Detect("Пацаны / The Boys (2022) WEB-DL 1080p | D, P, DV | Кубик в Кубе"));
    }

    [Fact]
    public void При_склейке_пометка_переносится_на_выжившее_название()
    {
        // Случай из жизни: инфохеш 59165c9b… — один и тот же файл, но кинозал
        // Dolby Vision не упомянул, а rutor упомянул. Выживает кинозаловское
        // название, и в Лампе раздача выглядела обычным HDR.
        string kinozal = "Пацаны (3 сезон: 1-8 серии из 8) / The Boys / 2022 / 4K, HEVC, HDR, HDR10+ / WEB-DL (2160p)";
        string rutor = "Пацаны / The Boys [S03] (2022) UHD WEB-DL-HEVC 2160p | 4K | HDR | Dolby Vision Profile 8";

        string got = DolbyVisionTag.Preserve(kinozal, rutor);

        Assert.EndsWith("| Dolby Vision", got);
        Assert.Equal(DolbyVisionKind.Dv, DolbyVisionTag.Detect(got));
    }

    [Fact]
    public void Пометка_TV_переносится_как_TV()
    {
        string got = DolbyVisionTag.Preserve(
            "Фильм / Film / 2022 / 4K, HDR10+ / WEB-DL (2160p)",
            "Фильм / Film / 2022 / 4K, HDR, Dolby Vision TV / Hybrid (2160p)");

        Assert.EndsWith("| Dolby Vision TV", got);
    }

    [Fact]
    public void Ничего_не_дописываем_если_признак_уже_есть()
    {
        string kept = "Фильм (2022) 4K Dolby Vision P8 WEB-DL";

        Assert.Equal(kept, DolbyVisionTag.Preserve(kept, "Фильм (2022) Dolby Vision TV"));
    }

    [Fact]
    public void Ничего_не_дописываем_если_у_поглощённой_копии_признака_нет()
    {
        string kept = "Фильм (2022) 4K HDR10+ WEB-DL";

        Assert.Equal(kept, DolbyVisionTag.Preserve(kept, "Фильм (2022) 4K HDR WEB-DL"));
    }
}
