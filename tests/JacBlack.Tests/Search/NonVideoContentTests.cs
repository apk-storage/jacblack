using JacBlack.Infrastructure.Parsing;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Отсев раздач, которые видео не являются.
///
/// Отбор по типу их не ловит: у nnmclub раздел назван словами, книжные мы не
/// опознаём, и работает правило «неопознанное считаем кино» — книга приходит
/// как movie. Замер 15.08.2026: 32 435 таких записей, 1.65% базы.
/// </summary>
public class NonVideoContentTests
{
    [Theory]
    [InlineData("Артемий Лебедев | Ководство (2014) [EPUB]")]
    [InlineData("И.В. Ивах и др. | Методика решения задач по физике (1969) [PDF] [Ukr]")]
    [InlineData("Люсинда Райли | Семь сестер [Книга 8]. Атлас (2023) [FB2]")]
    [InlineData("Олег Степанов | Энциклопедия первоклассника [DJVU]")]
    [InlineData("Проекты - VideoHive - Christmas Slideshow - 13523182 [AEP]")]
    [InlineData("Векторный клипарт - Дизайн футболок 6 / T-Shirt Design 6 [AI,EPS]")]
    [InlineData("Фотография - Bizart Magazine - Official Calendar 2014 [JPEG]")]
    [InlineData("Стоковые изображения - Creative Market - Golden Reverie Portraits")]
    public void Книги_картинки_и_шаблоны_отсеиваются(string title)
        => Assert.True(NonVideoContent.IsNotVideo(title));

    /// <summary>
    /// Обои и футажи приходят с пометками 4K и 1080p, то есть по признакам
    /// видео их не отличить — только по слову в названии.
    /// </summary>
    [Theory]
    [InlineData("Обои - Desktop Wallpapers (4K) Ultra HD. Part (304) [JPG]")]
    [InlineData("Футажи - Light and Energy Background Light Orb Love (1080p) [MOV]")]
    public void Обои_и_футажи_отсеиваются_несмотря_на_разрешение(string title)
        => Assert.True(NonVideoContent.IsNotVideo(title));

    /// <summary>
    /// Главная ловушка: [MP3] у фильма — это звуковая дорожка, а не альбом.
    /// Такие названия встречаются у испанских релизов целыми пачками, и
    /// прямолинейное правило «есть MP3 — не видео» выбросило бы фильмы.
    /// </summary>
    [Theory]
    [InlineData("Incidente 2008 [DVDRip][xivD][MP3][Spanish].avi")]
    [InlineData("El guardian de la memoria[DvDrip][xivD][MP3][Spanish]")]
    [InlineData("Air buddies 2006 su primera aventura[DVDRip][xivD][MP3][Spanish]")]
    public void Звук_рядом_с_признаками_видео_это_дорожка_фильма(string title)
        => Assert.False(NonVideoContent.IsNotVideo(title));

    [Theory]
    [InlineData("Никола Тесла | Утраченные изобретения [2011] [MP3]")]
    [InlineData("Сборник классики (2019) [FLAC]")]
    public void Звук_без_признаков_видео_это_музыка(string title)
        => Assert.True(NonVideoContent.IsNotVideo(title));

    /// <summary>
    /// `[DOC]` у нас встречается пометкой ДОКУМЕНТАЛЬНОГО фильма, а не
    /// вордовского файла — поэтому его в списке форматов нет.
    /// </summary>
    [Theory]
    [InlineData("[DOC] 911-In.Plane.Site.DirectorS.Cut.WEBRip.H264.AAC-BladeBDP")]
    [InlineData("Дом дракона / House of the Dragon [S03] (2026) WEB-DL 2160p")]
    [InlineData("Матрица / The Matrix (1999) BDRemux 1080p [MVO]")]
    [InlineData("Шимун / Simoun [TV] [26 из 26] [JAP+Sub]")]
    public void Настоящее_видео_не_трогаем(string title)
        => Assert.False(NonVideoContent.IsNotVideo(title));

    [Fact]
    public void Пустое_название_сомнению_не_подлежит()
    {
        Assert.False(NonVideoContent.IsNotVideo(null));
        Assert.False(NonVideoContent.IsNotVideo(""));
        Assert.False(NonVideoContent.IsNotVideo("   "));
    }
}
