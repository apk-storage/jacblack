using JacBlack.Infrastructure.Indexers;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Лампа берёт номер сезона из ТЕКСТА заголовка и понимает форму «N сезон»
/// (число впереди), но не «сезон N» (число в хвосте, за скобками с кодеком).
/// Раздачи второй формы пропадали из списка сезона в Лампе, хотя JacBlack их
/// отдавал (замер 07.08.2026: DV-раздача 3 сезона AV1 с nnmclub видна в вебе,
/// в Лампе нет). Нормализатор переставляет «сезон N» → «N сезон» на месте.
/// </summary>
public class SeasonTitleNormalizerTests
{
    [Fact]
    public void Хвостовую_форму_переставляет_в_голову()
    {
        // Тот самый заголовок, с которого всё началось.
        var got = SeasonTitleNormalizer.Normalize(
            "Пацаны / The Boys (2022) UHD WEB-DLRip [AV1/2160p] [4K, HDR10, DV Profile 10.1, 10-bit] (сезон 3, серии 1-8 из 8)");

        Assert.Contains("3 сезон", got);
        Assert.Contains("серии 1-8 из 8", got);      // хвост остаётся на месте
        Assert.DoesNotContain("сезон 3", got);
    }

    [Fact]
    public void Форму_с_двоеточием_тоже_переставляет()
    {
        Assert.Equal("Сериал 2 сезон", SeasonTitleNormalizer.Normalize("Сериал сезон: 2"));
        Assert.Equal("Сериал 2 сезон", SeasonTitleNormalizer.Normalize("Сериал сезон 2"));
    }

    [Fact]
    public void Готовую_голову_не_трогает()
    {
        // «N сезон» Лампа и так понимает — вмешиваться незачем, оставляем как есть.
        const string ok = "Пацаны (3 сезон: 1-8 серии из 8) / The Boys / 2022 / 4K, HEVC";
        Assert.Equal(ok, SeasonTitleNormalizer.Normalize(ok));
    }

    [Fact]
    public void Множественные_сезоны_не_ломает()
    {
        // «1-3 сезоны» — голова уже есть, тело не трогаем.
        const string many = "Реальные пацаны (1-3 сезоны: 1-70 серии из 70) / 2010";
        Assert.Equal(many, SeasonTitleNormalizer.Normalize(many));
    }

    [Fact]
    public void Без_сезона_в_тексте_оставляет_как_есть()
    {
        const string movie = "Дюна / Dune (2021) BDRip 1080p";
        Assert.Equal(movie, SeasonTitleNormalizer.Normalize(movie));
    }

    [Fact]
    public void Идемпотентен()
    {
        const string src = "The Boys (2022) [AV1/2160p] (сезон 3, серии 1-8)";
        var once = SeasonTitleNormalizer.Normalize(src);
        var twice = SeasonTitleNormalizer.Normalize(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Пустое_и_null_не_падают()
    {
        Assert.Equal("", SeasonTitleNormalizer.Normalize(""));
        Assert.Null(SeasonTitleNormalizer.Normalize(null));
    }
}
