using JacBlack.Infrastructure.Trackers.AnimeTosho;
using Xunit;

namespace JacBlack.Tests.AnimeTosho;

/// <summary>
/// Заголовки взяты из живой ленты feed.animetosho.org — все шесть форматов,
/// которые встретились при разведке 2026-07-26.
/// </summary>
public class AnimeToshoParserUnitTests
{
    [Fact]
    public void Убирает_ведущую_релиз_группу()
    {
        var r = AnimeToshoParser.ParseTitle("[Piyoko] Onegai AiPri - 05 [WEB AMZN 1080p h264 AC3 2.0]");
        Assert.Equal("Onegai AiPri", r.Name);
        Assert.Equal(5, r.Episode);
    }

    [Fact]
    public void Разбирает_сезон_и_серию()
    {
        var r = AnimeToshoParser.ParseTitle("[CrappySubs] MARRIAGETOXIN - S01E02 - (WEB 1080p H.264 AAC) [88B2A4D1]");
        Assert.Equal("MARRIAGETOXIN", r.Name);
        Assert.Equal(1, r.Season);
        Assert.Equal(2, r.Episode);
    }

    [Fact]
    public void Берёт_год_и_альтернативное_название()
    {
        var r = AnimeToshoParser.ParseTitle(
            "[Judas] Hokuto no Ken (2026) (Fist of the North Star) - S01E07 [1080p][HEVC x265 10bit][Dual-Audio]");
        Assert.Equal("Hokuto no Ken", r.Name);
        Assert.Equal(2026, r.Year);
        Assert.Equal("Fist of the North Star", r.OriginalName);
        Assert.Equal(1, r.Season);
        Assert.Equal(7, r.Episode);
    }

    [Fact]
    public void Сезон_без_серии_отрезается()
    {
        var r = AnimeToshoParser.ParseTitle("[Joseki] Turn A Gundam S01 (1999)(BD AV1 1080p Opus)[Sub Eng]");
        Assert.Equal("Turn A Gundam", r.Name);
        Assert.Equal(1999, r.Year);
        Assert.Equal(1, r.Season);
    }

    [Fact]
    public void Полнометражка_без_группы_сохраняет_имя_целиком()
    {
        var r = AnimeToshoParser.ParseTitle(
            "Lupin III - The Legend of the Gold of Babylon (1985) (BDRip 1920x1080p x265 HEVC OPUS 2.0x2)(Dual Audio)");
        Assert.Equal("Lupin III - The Legend of the Gold of Babylon", r.Name);
        Assert.Equal(1985, r.Year);
    }

    [Fact]
    public void Техническая_скобка_не_становится_альтернативным_названием()
    {
        var r = AnimeToshoParser.ParseTitle("[Group] Some Show (2024) (WEB 1080p x265 AAC) - S01E01");
        Assert.Equal("Some Show", r.Name);
        Assert.Equal(2024, r.Year);
        // Альтернативного названия нет — originalname повторяет name.
        Assert.Equal("Some Show", r.OriginalName);
    }

    [Fact]
    public void Сезон_и_серия_без_дефиса_и_скобок_отрезаются()
    {
        // Сценовое именование: ни дефиса, ни скобок. На этом парсер спотыкался
        // при первом прогоне живой ленты 2026-07-26.
        var r = AnimeToshoParser.ParseTitle("Petals of Reincarnation S01E06 1080p AMZN WEB-DL AAC2.0 H 264-Vary");
        Assert.Equal("Petals of Reincarnation", r.Name);
        Assert.Equal(1, r.Season);
        Assert.Equal(6, r.Episode);
    }

    [Fact]
    public void Качество_не_попадает_в_имя()
    {
        var r = AnimeToshoParser.ParseTitle("Some Anime Title 1080p BDRip x265");
        Assert.Equal("Some Anime Title", r.Name);
    }

    [Fact]
    public void Источник_WEB_DL_тоже_граница_имени()
    {
        var r = AnimeToshoParser.ParseTitle("Another Show WEB-DL 720p");
        Assert.Equal("Another Show", r.Name);
    }

    [Fact]
    public void Пустой_заголовок_не_роняет_разбор()
    {
        var r = AnimeToshoParser.ParseTitle("");
        Assert.Equal("", r.Name);
        Assert.Equal(0, r.Year);
    }

    [Fact]
    public void Без_magnet_запись_отбрасывается()
    {
        var item = new AnimeToshoItem
        {
            Id = 1,
            Title = "[Group] Show - S01E01 [1080p]",
            MagnetUri = null,
            Status = "complete"
        };
        Assert.Null(AnimeToshoParser.MapToTorrentDetails(item));
    }

    [Fact]
    public void Незавершённая_раздача_отбрасывается()
    {
        var item = new AnimeToshoItem
        {
            Id = 2,
            Title = "[Group] Show - S01E01 [1080p]",
            MagnetUri = "magnet:?xt=urn:btih:abc",
            Status = "skipped"
        };
        Assert.Null(AnimeToshoParser.MapToTorrentDetails(item));
    }

    [Fact]
    public void Заполняет_запись_целиком()
    {
        var item = new AnimeToshoItem
        {
            Id = 764689,
            Title = "[Judas] Hokuto no Ken (2026) (Fist of the North Star) - S01E07 [1080p]",
            Link = "https://animetosho.org/view/hokuto-no-ken-s01e07",
            MagnetUri = "magnet:?xt=urn:btih:VUSGIESK7OWHR22H3BZMSCIHLHC5EER6",
            Seeders = 21,
            Leechers = 30,
            TotalSize = 7680159471,
            Timestamp = 1778284611,
            Status = "complete",
            AnidbAid = 989
        };

        var t = AnimeToshoParser.MapToTorrentDetails(item);

        Assert.NotNull(t);
        Assert.Equal("animetosho", t.trackerName);
        Assert.Equal(new[] { "anime" }, t.types);
        // Адрес строится из числового id, а не из slug: slug на сайте меняется.
        Assert.Equal("https://animetosho.org/view/764689", t.url);
        Assert.Equal("Hokuto no Ken", t.name);
        Assert.Equal("Fist of the North Star", t.originalname);
        Assert.Equal(2026, t.relased);
        Assert.Equal(21, t.sid);
        Assert.Equal(30, t.pir);
        Assert.StartsWith("magnet:?xt=urn:btih:", t.magnet);
        Assert.Contains("ГБ", t.sizeName);
    }

    [Fact]
    public void Адрес_строится_из_идентификатора_а_не_из_slug()
    {
        var item = new AnimeToshoItem
        {
            Id = 42,
            Link = "https://animetosho.org/view/some-slug-that-can-change",
            Title = "[Group] Show - 01 [1080p]",
            MagnetUri = "magnet:?xt=urn:btih:abc",
            Status = "complete"
        };

        var t = AnimeToshoParser.MapToTorrentDetails(item);

        Assert.NotNull(t);
        Assert.Equal("https://animetosho.org/view/42", t.url);
    }

    [Fact]
    public void Без_идентификатора_остаётся_ссылка_из_ленты()
    {
        var item = new AnimeToshoItem
        {
            Id = 0,
            Link = "https://animetosho.org/view/fallback",
            Title = "[Group] Show - 01 [1080p]",
            MagnetUri = "magnet:?xt=urn:btih:abc",
            Status = "complete"
        };

        Assert.Equal("https://animetosho.org/view/fallback", AnimeToshoParser.MapToTorrentDetails(item).url);
    }
}
