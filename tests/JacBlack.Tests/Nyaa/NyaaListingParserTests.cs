using System.Linq;
using JacBlack.Infrastructure.Trackers.Nyaa;
using Xunit;

namespace JacBlack.Tests.Nyaa;

/// <summary>
/// Разбор HTML-листинга nyaa на настоящем снимке страницы (раздел 1_2,
/// снят 19.08.2026).
///
/// Листинг понадобился потому, что лента упирается в потолок: сто страниц
/// свежего и всё, параметры сортировки она игнорирует, а при поиске не смотрит
/// на номер страницы. HTML те же параметры понимает — отсюда путь вглубь.
/// </summary>
public class NyaaListingParserTests
{
    static string Страница() => FixtureLoader.Read("Nyaa/listing-1_2.html");

    [Fact]
    public void Со_страницы_читаются_все_семьдесят_пять_раздач()
    {
        var items = NyaaListingParser.Parse(Страница());

        Assert.Equal(75, items.Count);
    }

    [Fact]
    public void У_каждой_записи_есть_то_без_чего_она_бесполезна()
    {
        var items = NyaaListingParser.Parse(Страница());

        Assert.All(items, i =>
        {
            Assert.False(string.IsNullOrWhiteSpace(i.Title), "нет названия");
            Assert.False(string.IsNullOrWhiteSpace(i.InfoHash), "нет хеша — magnet не собрать");
            Assert.Contains("/view/", i.ViewUrl);
            Assert.NotEqual(default, i.PubDate);
        });
    }

    [Fact]
    public void Название_берётся_из_раздачи_а_не_из_счётчика_комментариев()
    {
        // В строке две ссылки на /view/: первая — число комментариев. Если
        // взять её, в название поедет «1», и запись молча испортится.
        var items = NyaaListingParser.Parse(Страница());

        Assert.All(items, i => Assert.False(
            int.TryParse(i.Title.Trim(), out _), $"в название попал счётчик: «{i.Title}»"));
    }

    [Fact]
    public void Размер_и_счётчики_разобраны()
    {
        var items = NyaaListingParser.Parse(Страница());
        var first = items[0];

        Assert.Matches(@"^\d+(\.\d+)?\s*(B|KiB|MiB|GiB|TiB)$", first.SizeName);
        Assert.True(items.Any(i => i.Seeders > 0), "ни у одной раздачи нет сидов — колонки разъехались");
        Assert.All(items, i => Assert.True(i.Seeders >= 0 && i.Leechers >= 0));
    }

    [Fact]
    public void Раздел_опознан()
    {
        var items = NyaaListingParser.Parse(Страница());

        Assert.All(items, i => Assert.Equal("1_2", i.CategoryId));
    }

    [Fact]
    public void Запись_превращается_в_раздачу_тем_же_путём_что_и_из_ленты()
    {
        // Сборка TorrentDetails общая с лентой — так magnet, тип и разбор
        // названия остаются одинаковыми, откуда бы запись ни пришла.
        var items = NyaaListingParser.Parse(Страница());
        var torrents = NyaaParser.ParseTorrents(items);

        Assert.NotEmpty(torrents);
        Assert.All(torrents, t =>
        {
            Assert.Equal("nyaa", t.trackerName);
            Assert.Contains("magnet:?xt=urn:btih:", t.magnet);
            Assert.False(string.IsNullOrWhiteSpace(t.name));
        });
    }

    [Fact]
    public void Страница_за_пределом_выдачи_даёт_пусто_а_не_мусор()
    {
        // За сотой страницей nyaa отдаёт документ без таблицы. Обход по этому
        // признаку и останавливается, поэтому пустой список тут обязателен.
        var items = NyaaListingParser.Parse("<html><body><p>No results found</p></body></html>");

        Assert.Empty(items);
    }
}
