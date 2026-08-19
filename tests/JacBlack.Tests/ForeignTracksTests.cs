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

    [Theory]
    [InlineData("Game of Thrones S02E08 The Prince of Winterfell 1080p BluRay x264")]   // «Inter» внутри Winterfell
    [InlineData("[NanakoRaws] Otonari no Tenshi-sama ni Itsunomanika 1080p")]           // «Anika» внутри Itsunomanika
    [InlineData("[HnY] Beyblade Burst DB 27 - The Joyful Demon King's Big Step")]       // «DeMon» внутри Demon
    [InlineData("School Rumble (1080p)(AI Enhanced)")]                                  // «Rumble» — само название
    public void Студия_не_находится_внутри_чужого_слова(string title)
    {
        // Список из ~900 студий искался обычной подстрокой, и человек видел в
        // Лампе озвучку, которой нет. Все четыре примера — с живой выдачи.
        var t = Разобрать(Раздача(title));

        Assert.DoesNotContain(t.voices, v => v != "Две дорожки" && v != "Мультиязычная" && v != "Субтитры");
    }

    [Fact]
    public void Настоящая_студия_по_прежнему_находится()
    {
        // Границы слова не должны мешать обычному случаю: у студий в названиях
        // сплошь точки и дефисы.
        var t = Разобрать(Раздача("Сериал / Series (2024) WEB-DL 1080p [LostFilm.TV]", "rutor"));

        Assert.Contains("LostFilm", t.voices);
    }

    [Fact]
    public void Языковая_разметка_toloka_разбирается()
    {
        // Так подписывает раздачи toloka: две украинские дорожки, английская,
        // английские субтитры. Раньше всё это пропадало — поле языков было
        // пустым, хотя в названии написано прямым текстом.
        var t = Разобрать(Раздача(
            "Дюна / Dune: Part One (2021) BDRip-AVC 2xUkr/Eng | Sub Eng", "toloka"));

        Assert.Contains("ukr", t.languages);
        Assert.Contains("eng", t.languages);
        Assert.Contains("Две дорожки", t.voices);
        Assert.Contains("Субтитры", t.voices);
    }

    [Fact]
    public void Одна_дорожка_не_объявляется_двумя()
    {
        var t = Разобрать(Раздача("Фільм / Film (2024) BDRip Ukr", "toloka"));

        Assert.Contains("ukr", t.languages);
        Assert.DoesNotContain("Две дорожки", t.voices);
    }

    [Fact]
    public void Пустое_название_ничего_не_выдумывает()
    {
        var t = Разобрать(Раздача("Some Movie 2024 1080p WEBRip"));

        Assert.Empty(t.voices);
        Assert.DoesNotContain("eng", t.languages ?? new System.Collections.Generic.HashSet<string>());
    }
}
