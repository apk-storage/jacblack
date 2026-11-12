using JacBlack.Infrastructure.Persistence;
using JacBlack.Models.Details;
using Xunit;

namespace JacBlack.Tests;

/// <summary>
/// Раздача, где русские только субтитры, не должна проходить фильтр «Русский».
///
/// 12.11.2026 в чате сравнили с jac.red: «Убийца-психопат» (2026) русской
/// озвучки не имеет нигде, а у нас под фильтром «Русский» показывались три
/// раздачи с одними субтитрами — правило «русский трекер + кириллица» считало
/// их русскими. Названия ниже — живые, из нашей базы.
/// </summary>
public class SubsOnlyTests
{
    static TorrentDetails Разобрать(string title, string tracker)
    {
        var t = new TorrentDetails
        {
            title = title,
            trackerName = tracker,
            types = new[] { "movie" },
            magnet = "magnet:?xt=urn:btih:0000000000000000000000000000000000000000",
            createTime = new System.DateTime(2026, 1, 1),
        };
        FileDB.updateFullDetails(t);
        return t;
    }

    [Theory]
    [InlineData("Убийца-психопат / Psycho Killer / 2026 / СТ / 4K, HEVC, HDR10+ / WEB-DL (2160p)", "kinozal")]
    [InlineData("Убийца-психопат / Psycho Killer (2026) WEB-DL 1080p | Sub", "rutor")]
    [InlineData("Убийца-психопат / Psycho Killer (2026) WEB-DL [H.265/2160p] [4K, HDR10+, 10-bit] [EN / RU, EN Sub]", "nnmclub")]
    public void Только_субтитры_не_русская_дорожка(string title, string tracker)
    {
        var t = Разобрать(title, tracker);

        Assert.DoesNotContain("rus", t.languages);
        Assert.Contains("Субтитры", t.voices);
    }

    [Theory]
    [InlineData("Король Талсы (3 сезон: 1-10 серии из 10) / Tulsa King / 2025 / ПМ (LostFilm), СТ / WEB-DLRip", "kinozal")]
    [InlineData("Король Талсы (3 сезон: 1-10 серии из 10) / Tulsa King / 2025 / 6 x ПМ, ЛМ, АП (Яроцкий), СТ / WEB-DL (720p)", "kinozal")]
    [InlineData("Убийца-психопат / Psycho Killer / 2026 / ДБ / TS", "kinozal")]
    [InlineData("Король Талсы / Tulsa King [S03] (2025) WEB-DL 1080p | P | TVShows", "rutor")]
    [InlineData("Король Талсы / Tulsa King [S02] (2024) WEB-DL 1080p | P, A, L", "rutor")]
    [InlineData("Фильм / Film (2026) WEB-DL [H.264/1080p] [RU, EN / RU, EN Sub]", "nnmclub")]
    public void Озвучка_рядом_с_субтитрами_остаётся_русской(string title, string tracker)
    {
        var t = Разобрать(title, tracker);

        Assert.Contains("rus", t.languages);
    }

    [Theory]
    // Молчание названия — не «только субтитры»: прежнее правило трекера работает.
    [InlineData("Король Талсы 2 сезон (1-10 из 10) / Tulsa King (2024) WEB-DLRip", "bitru")]
    [InlineData("Король Талсы 1-3 сезон (1-29 из 29) / Tulsa King (2022-2025) WEB-DLRip | КПК", "bitru")]
    public void Без_пометок_как_раньше(string title, string tracker)
    {
        var t = Разобрать(title, tracker);

        Assert.Contains("rus", t.languages);
        Assert.False(FileDB.ТолькоСубтитры(title.ToLowerInvariant()));
    }
}
