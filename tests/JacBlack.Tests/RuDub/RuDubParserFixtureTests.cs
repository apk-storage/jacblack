using System.Linq;
using JacBlack.Infrastructure.Trackers.RuDub;
using Xunit;

namespace JacBlack.Tests.RuDub;

/// <summary>
/// Снимок списка r4.rudub.world от 08.10.2026.
///
/// Трекер добавлен после того, как в jac.black не нашёлся сериал «Кэрри»
/// 2026: он лежит именно здесь. Движок как у kinozal, но торрент отдаётся
/// без входа, а инфо-хеша на страницах нет вовсе — его считают из файла.
///
/// Снимок снят ГОСТЕМ: так же ходит и обход, и если однажды гостю перестанут
/// показывать список, тест упадёт первым.
/// </summary>
public class RuDubParserFixtureTests
{
    static string Html() => FixtureLoader.Read("RuDub/list_page1.html");

    [Fact]
    public void Со_страницы_списка_разбираются_раздачи()
    {
        var list = RuDubParser.ParseTorrentsFromListPage(Html());

        Assert.NotNull(list);
        Assert.True(list.Count >= 20, $"ожидали хотя бы 20 раздач, получили {list.Count}");
    }

    [Fact]
    public void У_каждой_раздачи_есть_всё_нужное_для_базы()
    {
        foreach (var t in RuDubParser.ParseTorrentsFromListPage(Html()))
        {
            Assert.Equal("rudub", t.trackerName);
            Assert.False(string.IsNullOrWhiteSpace(t.url), "нет адреса раздачи");
            Assert.Contains("details.php?id=", t.url);
            Assert.False(string.IsNullOrWhiteSpace(t.title), "нет заголовка");
            Assert.False(string.IsNullOrWhiteSpace(t.name), $"нет имени у «{t.title}»");
            Assert.False(string.IsNullOrWhiteSpace(t.sizeName), $"нет размера у «{t.title}»");
            Assert.NotEqual(default, t.createTime);
        }
    }

    [Fact]
    public void Дата_разбирается_в_нужном_виде()
    {
        var t = RuDubParser.ParseTorrentsFromListPage(Html()).First();

        // В списке «2026-10-07 21:37:08» — не европейский формат соседних
        // трекеров, и перепутать их легко.
        Assert.Equal(2026, t.createTime.Year);
        Assert.True(t.createTime.Month >= 1 && t.createTime.Month <= 12);
    }

    [Fact]
    public void Сиды_и_личи_берутся_из_одной_строки_активности()
    {
        var list = RuDubParser.ParseTorrentsFromListPage(Html());

        // Оба числа стоят в «Активность» подряд, и если их перепутать
        // местами или слить, у всех раздач выйдет одинаковое значение.
        Assert.Contains(list, t => t.sid > 0);
        Assert.True(list.All(t => t.sid >= 0 && t.pir >= 0));
    }

    [Theory]
    [InlineData("Морская Полиция: Истоки (NCIS: Origins) Сезон 3 Серии 01 (HD720p WEBRip)",
                "Морская Полиция: Истоки", "NCIS: Origins")]
    [InlineData("Вампир Лестат (Интервью с вампиром 3)", "Вампир Лестат", null)]
    [InlineData("Кэрри (Carrie) Сезон 1 Серии 01-03 (WEBRip)", "Кэрри", "Carrie")]
    public void Имя_и_оригинальное_имя_разделяются(string title, string name, string original)
    {
        var (разобрано, оригинал) = RuDubParser.SplitTitle(title);

        Assert.Equal(name, разобрано);
        Assert.Equal(original, оригинал);
    }

    [Fact]
    public void Русские_скобки_за_оригинальное_имя_не_принимаются()
    {
        // «(Интервью с вампиром 3)» — пояснение, а не оригинальное название:
        // латиницы в нём нет. Без этой проверки в поле originalname попадал
        // бы русский текст, и поиск по оригиналу ломался бы.
        var (_, оригинал) = RuDubParser.SplitTitle("Вампир Лестат (Интервью с вампиром 3)");

        Assert.Null(оригинал);
    }

    [Theory]
    [InlineData("Кэрри (Carrie) Сезон 1 Серии 01-03", "serial")]
    [InlineData("Морская Полиция Сезон 3 Серия 01", "serial")]
    [InlineData("Какой-то фильм (Some Movie) (WEBRip)", "movie")]
    public void Сериал_отличается_от_фильма_по_названию(string title, string ожидаем)
    {
        Assert.Equal(new[] { ожидаем }, RuDubParser.GuessTypes(title));
    }

    [Fact]
    public void Адрес_торрента_строится_из_адреса_раздачи()
    {
        // Именно download2, а не download: на второй трекер отвечает отказом.
        Assert.Equal("/download2.php?id=55583", RuDubParser.TorrentUrl("details.php?id=55583"));
        Assert.Equal("/download2.php?id=1", RuDubParser.TorrentUrl("/details.php?id=1&page=2"));
        Assert.Null(RuDubParser.TorrentUrl("browse.php"));
    }

    [Fact]
    public void Число_страниц_берётся_из_нумерации()
    {
        // На снимке 1648 страниц — около 51 тысячи раздач.
        Assert.True(RuDubParser.LastPage(Html()) > 100,
            "нумерация страниц не разобралась, обход возьмёт только первую");
    }

    [Fact]
    public void Адрес_раздачи_становится_полным_когда_известен_хост()
    {
        // Ключ записи в базе — ПОЛНЫЙ адрес. С относительным
        // «details.php?id=…» запись не сохраняется вовсе: 08.10.2026 первый
        // обход разобрал 150 раздач и не положил ни одной.
        var список = RuDubParser.ParseTorrentsFromListPage(Html(), "https://r4.rudub.world");

        Assert.All(список, t => Assert.StartsWith("https://r4.rudub.world/details.php?id=", t.url));
    }

    [Fact]
    public void Без_хоста_адрес_остаётся_как_на_странице()
    {
        var список = RuDubParser.ParseTorrentsFromListPage(Html());

        Assert.All(список, t => Assert.Contains("details.php?id=", t.url));
    }

    [Fact]
    public void Пустой_ввод_не_роняет_разбор()
    {
        Assert.Empty(RuDubParser.ParseTorrentsFromListPage(null));
        Assert.Empty(RuDubParser.ParseTorrentsFromListPage(""));
        Assert.Equal(1, RuDubParser.LastPage(null));
        Assert.Equal((null, null), RuDubParser.SplitTitle(null));
    }
}
