using System.Linq;
using JacBlack.Infrastructure.Indexers;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Раскладка типов раздач по категориям Newznab.
///
/// Закрепляется тестами потому, что она влияет не только на подпись:
/// запись БЕЗ категории проходит любой фильтр (см. FilterByCategory),
/// а с категорией — только свой раздел. Ошибка здесь тихо прячет раздачи
/// от клиентов вроде Prowlarr.
/// </summary>
public class IndexerCategoryTests
{
    static int[] Cats(string type) => IndexerSearchEngine.TypeToCategory[type].cats;

    [Theory]
    [InlineData("movie")]
    [InlineData("documovie")]
    [InlineData("multfilm")]
    public void Фильмы_попадают_в_раздел_фильмов(string type)
    {
        Assert.Contains(2000, Cats(type));
        Assert.DoesNotContain(Cats(type), c => c >= 5000 && c < 6000);
    }

    [Theory]
    [InlineData("serial")]
    [InlineData("multserial")]
    [InlineData("tvshow")]
    [InlineData("docuserial")]
    public void Сериалы_попадают_в_раздел_тв(string type)
    {
        Assert.Contains(Cats(type), c => c >= 5000 && c < 6000);
        Assert.DoesNotContain(2000, Cats(type));
    }

    [Fact]
    public void Документальный_сериал_помечен_и_как_документалистика()
    {
        Assert.Contains(5000, Cats("docuserial"));
        Assert.Contains(5080, Cats("docuserial"));
    }

    [Fact]
    public void Аниме_и_спорт_имеют_свои_категории()
    {
        Assert.Equal(new[] { 5070 }, Cats("anime"));
        Assert.Equal(new[] { 5060 }, Cats("sport"));
    }

    [Fact]
    public void Все_известные_типы_раздач_имеют_категорию()
    {
        // Список взят из JackettCardMatcher — там перечислены все типы,
        // которые вообще встречаются в базе.
        string[] known = { "movie", "serial", "anime", "documovie", "docuserial", "tvshow", "multfilm", "multserial", "sport" };

        foreach (var type in known)
            Assert.True(IndexerSearchEngine.TypeToCategory.ContainsKey(type), $"тип {type} остался без категории");
    }

    [Fact]
    public void Каждая_категория_объявлена_клиентам()
    {
        // Категория, которой нет в списке для Prowlarr, приедет клиенту
        // безымянной — так было бы с 5060, пока её не добавили.
        var declared = new[] { 2000, 2010, 5000, 5020, 5060, 5070, 5080 };

        foreach (var pair in IndexerSearchEngine.TypeToCategory)
            foreach (int c in pair.Value.cats)
                Assert.Contains(c, declared);
    }
}
