using System.Linq;
using JacBlack.Infrastructure.Persistence;
using JacBlack.Models.Details;
using Xunit;

namespace JacBlack.Tests;

/// <summary>
/// Дорожки у зарубежных раздач.
///
/// Люди спросили 19.08.2026, почему у piratebay в Лампе не видно ничего, кроме
/// разрешения, тогда как у русских трекеров показаны и озвучка, и качество.
/// Проверка показала: разрешение у них определяется, а вот озвучка пустая —
/// и это не наш недосмотр, русской дорожки в таких релизах нет вовсе.
///
/// Но кое-что в названии написано почти всегда: сколько дорожек, на каком
/// языке и есть ли субтитры. Теперь это разбирается. Важно, что берётся ровно
/// написанное: выдумывать «Оригинал» там, где источник молчит, нельзя —
/// на таких константах мы уже обжигались.
/// </summary>
public class ForeignTracksTests
{
    static TorrentDetails Раздача(string title, string tracker = "piratebay") => new()
    {
        title = title,
        trackerName = tracker,
        types = new[] { "movie" },
        magnet = "magnet:?xt=urn:btih:0000000000000000000000000000000000000000",
        createTime = new System.DateTime(2024, 1, 1),
    };

    static TorrentDetails Разобрать(TorrentDetails t)
    {
        FileDB.updateFullDetails(t);
        return t;
    }

    [Fact]
    public void Две_дорожки_опознаются()
    {
        var t = Разобрать(Раздача("Some Movie (2024) [1080p] Dual Audio [BluRay]"));

        Assert.Contains("Две дорожки", t.voices);
    }

    [Fact]
    public void Мультиязычная_раздача_опознаётся()
    {
        var t = Разобрать(Раздача("Some Movie 2024 MULTI 1080p BluRay x264"));

        Assert.Contains("Мультиязычная", t.voices);
    }

    [Fact]
    public void Субтитры_не_путаются_с_озвучкой()
    {
        // «Multi-Subs» — это субтитры, а не дорожки. Раньше слово «multi»
        // засчиталось бы как мультиязычный звук.
        var t = Разобрать(Раздача("[Group] Anime - 07 [1080p][Multi-Subs]", "nyaa"));

        Assert.Contains("Субтитры", t.voices);
        Assert.DoesNotContain("Мультиязычная", t.voices);
    }

    [Fact]
    public void Язык_оригинала_берётся_из_названия()
    {
        var t = Разобрать(Раздача("Game of Thrones S08 2019 1080p BluRay ENG AAC5.1 x264"));

        Assert.Contains("eng", t.languages);
    }

    [Fact]
    public void Английские_субтитры_не_делают_дорожку_английской()
    {
        // «ENG Sub» говорит о субтитрах, а не о звуке: у аниме дорожка при
        // этом японская.
        var t = Разобрать(Раздача("[Group] Anime - 05 [1080p][JPN][ENG Subs]", "nyaa"));

        Assert.Contains("jpn", t.languages);
        Assert.DoesNotContain("eng", t.languages);
        Assert.Contains("Субтитры", t.voices);
    }

    [Fact]
    public void Русские_раздачи_разбираются_как_прежде()
    {
        // Правка не должна задеть то, ради чего поле и заводилось.
        // Название взято в том виде, в каком его пишет rutor: вид перевода
        // стоит в перечислении, а не в конце строки — разбор ждёт разделитель.
        var t = Разобрать(Раздача(
            "Дюна: Часть вторая / Dune: Part Two (2024) BDRip 1080p, Дубляж, Многоголосый", "rutor"));

        Assert.Contains("Дубляж", t.voices);
        Assert.Contains("rus", t.languages);
    }

    [Fact]
    public void Пустое_название_ничего_не_выдумывает()
    {
        var t = Разобрать(Раздача("Some Movie 2024 1080p WEBRip"));

        Assert.Empty(t.voices);
        Assert.DoesNotContain("eng", t.languages ?? new System.Collections.Generic.HashSet<string>());
    }
}
