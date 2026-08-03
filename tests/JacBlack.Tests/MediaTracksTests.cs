using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Models.Tracks;
using Xunit;

namespace JacBlack.Tests;

/// <summary>
/// Сводка дорожек для карточки: кодек картинки, кодеки звука, разбор по
/// дорожкам из ffprobe.
///
/// Главное правило, которое здесь и закрепляется: из заголовка мы НЕ выводим,
/// какая студия озвучила в каком кодеке — этого там нет. Пары отдаются только
/// когда есть ffprobe, иначе получилась бы правдоподобная выдумка.
/// </summary>
public class MediaTracksTests
{
    [Theory]
    [InlineData("Дюна / Dune (2021) BDRemux 2160p HEVC", "x265")]
    [InlineData("Movie.2024.1080p.WEB-DL.x265-GROUP", "x265")]
    [InlineData("Movie.2024.1080p.BluRay.H.264", "x264")]
    [InlineData("Movie.2024.2160p.AV1.WEB", "av1")]
    [InlineData("Старое кино (1999) DVDRip XviD", "xvid")]
    public void Кодек_картинки_читается_из_заголовка(string title, string expected)
        => Assert.Equal(expected, MediaTracks.VideoFromTitle(title));

    [Fact]
    public void Синонимы_кодека_сводятся_к_одному_написанию()
    {
        // HEVC, h.265 и x265 — одно и то же. Три разные плашки на карточке
        // только мешали бы.
        Assert.Equal("x265", MediaTracks.VideoFromTitle("Film 2160p HEVC"));
        Assert.Equal("x265", MediaTracks.VideoFromTitle("Film 2160p h.265"));
        Assert.Equal("x265", MediaTracks.VideoFromTitle("Film 2160p x265"));
    }

    [Fact]
    public void Длинное_имя_кодека_не_теряется_из_за_короткого()
    {
        // Порядок проверки важен: DTS-HD должен опознаться раньше DTS,
        // иначе в выдачу уйдёт огрублённое имя.
        var audio = MediaTracks.AudioFromTitle("Movie 2160p DTS-HD MA 5.1");
        Assert.Contains("dts-hd", audio);
        Assert.DoesNotContain("dts", audio);

        var eac = MediaTracks.AudioFromTitle("Movie 1080p DDP5.1");
        Assert.Contains("eac3", eac);
        Assert.DoesNotContain("ac3", eac);
    }

    [Fact]
    public void Несколько_кодеков_звука_читаются_вместе()
    {
        var audio = MediaTracks.AudioFromTitle("Фильм (2024) BDRip | AC3 + AAC");
        Assert.Contains("ac3", audio);
        Assert.Contains("aac", audio);
    }

    [Fact]
    public void Без_кодеков_в_заголовке_сводка_пуста()
    {
        var s = MediaTracks.Build(null, "Просто название без обозначений");
        Assert.True(s.IsEmpty);
        Assert.Null(s.audio);
        Assert.Null(s.video);
    }

    static ffStream Audio(string codec, string lang, int channels, string title = null) => new()
    {
        codec_type = "audio",
        codec_name = codec,
        channels = channels,
        tags = new ffTags { language = lang, title = title }
    };

    [Fact]
    public void Ffprobe_даёт_дорожки_с_языком_и_каналами()
    {
        var s = MediaTracks.Build(new List<ffStream>
        {
            new() { codec_type = "video", codec_name = "hevc" },
            Audio("eac3", "rus", 6, "Дубляж"),
            Audio("aac", "eng", 2),
            new() { codec_type = "subtitle", codec_name = "subrip", tags = new ffTags { language = "rus" } }
        }, "Фильм (2024) 2160p");

        Assert.Equal("x265", s.video);
        Assert.Equal(2, s.tracks.Count);

        var ru = s.tracks[0];
        Assert.Equal("eac3", ru.codec);
        Assert.Equal("ru", ru.language);
        Assert.Equal(6, ru.channels);
        Assert.Equal("Дубляж", ru.title);

        Assert.Equal(new[] { "ru" }, s.subtitles);
    }

    [Fact]
    public void Ffprobe_главнее_заголовка()
    {
        // В заголовке написано x264, а в файле на самом деле HEVC.
        // Верим файлу: заголовки врут регулярно.
        var s = MediaTracks.Build(new List<ffStream>
        {
            new() { codec_type = "video", codec_name = "hevc" }
        }, "Фильм (2024) 1080p x264");

        Assert.Equal("x265", s.video);
    }

    [Fact]
    public void Код_языка_приводится_к_двум_буквам()
    {
        var s = MediaTracks.Build(new List<ffStream> { Audio("ac3", "rus", 6), Audio("ac3", "ru", 6) }, null);
        Assert.All(s.tracks, t => Assert.Equal("ru", t.language));
    }

    [Theory]
    [InlineData("heb", "he")]
    [InlineData("por", "pt")]
    [InlineData("ces", "cs")]
    [InlineData("nld", "nl")]
    [InlineData("ell", "el")]
    public void Трёхбуквенные_коды_приводятся_к_двум_а_не_обрезаются(string code, string expected)
    {
        // Обрезать нельзя: por → pt, а не po. Без явного перечня на карточке
        // выходил разнобой — часть языков двумя буквами, часть тремя.
        var s = MediaTracks.Build(new List<ffStream> { Audio("ac3", code, 6) }, null);
        Assert.Equal(expected, s.tracks[0].language);
    }

    [Fact]
    public void Неизвестный_трёхбуквенный_код_лучше_не_показать_чем_показать_криво()
    {
        var s = MediaTracks.Build(new List<ffStream> { Audio("ac3", "qqq", 6) }, null);
        Assert.Null(s.tracks[0].language);
    }

    [Fact]
    public void Неизвестный_язык_не_показываем()
    {
        var s = MediaTracks.Build(new List<ffStream> { Audio("ac3", "und", 6) }, null);
        Assert.Null(s.tracks[0].language);
    }

    [Fact]
    public void При_наличии_дорожек_набор_из_заголовка_уступает_им()
    {
        // Иначе карточка показала бы и «ac3» из заголовка, и «eac3» из файла,
        // хотя дорожка одна.
        var s = MediaTracks.Build(new List<ffStream> { Audio("eac3", "rus", 6) }, "Фильм (2024) AC3");

        Assert.Equal(new[] { "eac3" }, s.audio);
    }
}
