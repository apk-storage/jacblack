using JacBlack.Infrastructure.Persistence;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Словарь «код IMDB → название» — то, чем поиск по идентификатору отвечает
/// без обращения к чужому сервису.
/// </summary>
public class ImdbIndexTests
{
    [Fact]
    public void Запоминает_и_находит()
    {
        ImdbIndex.Remember("tt0816692", "Интерстеллар", "Interstellar", 2014);

        Assert.True(ImdbIndex.TryGet("tt0816692", out var found));
        Assert.Equal("Интерстеллар", found.Name);
        Assert.Equal("Interstellar", found.OriginalName);
        Assert.Equal(2014, found.Year);
    }

    [Fact]
    public void Регистр_кода_не_важен()
    {
        ImdbIndex.Remember("tt1160419", "Дюна", "Dune", 2021);

        Assert.True(ImdbIndex.TryGet("TT1160419", out _));
    }

    [Fact]
    public void Первый_источник_не_перезаписывается()
    {
        // Уже известное не трогаем: первый источник обычно не хуже следующего,
        // а лишние записи на диск ни к чему.
        ImdbIndex.Remember("tt9999991", "Первое", "First", 2001);
        ImdbIndex.Remember("tt9999991", "Второе", "Second", 2002);

        Assert.True(ImdbIndex.TryGet("tt9999991", out var found));
        Assert.Equal("Первое", found.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tt0000000")]
    public void Неизвестное_не_находится(string code)
    {
        Assert.False(ImdbIndex.TryGet(code, out _));
    }

    [Fact]
    public void Находит_код_по_названию_и_году()
    {
        // Так код подтягивается к раздачам источников, которые его не сообщают:
        // код принесла одна раздача — остальные находят его по совпадению.
        ImdbIndex.Remember("tt0111161", "Побег из Шоушенка", "The Shawshank Redemption", 1994);

        Assert.True(ImdbIndex.TryGetByTitle("The Shawshank Redemption", 1994, out string byOriginal));
        Assert.Equal("tt0111161", byOriginal);

        Assert.True(ImdbIndex.TryGetByTitle("Побег из Шоушенка", 1994, out string byName));
        Assert.Equal("tt0111161", byName);
    }

    [Fact]
    public void Знаки_и_регистр_в_названии_не_мешают()
    {
        ImdbIndex.Remember("tt1160419", "Дюна", "Dune: Part One", 2021);

        Assert.True(ImdbIndex.TryGetByTitle("dune part one", 2021, out string code));
        Assert.Equal("tt1160419", code);
    }

    [Fact]
    public void Год_обязателен_иначе_склеятся_разные_фильмы()
    {
        // «Дюна» 1984 года и «Дюна» 2021-го — разные фильмы с одним названием.
        ImdbIndex.Remember("tt0087182", "Дюна", "Dune", 1984);
        ImdbIndex.Remember("tt15239678", "Дюна: Часть вторая", "Dune", 2024);

        Assert.True(ImdbIndex.TryGetByTitle("Dune", 1984, out string old));
        Assert.True(ImdbIndex.TryGetByTitle("Dune", 2024, out string recent));

        Assert.Equal("tt0087182", old);
        Assert.Equal("tt15239678", recent);
        Assert.False(ImdbIndex.TryGetByTitle("Dune", 1999, out _));
    }

    [Theory]
    [InlineData("Interstellar", 0)]
    [InlineData("Interstellar", 1899)]
    [InlineData(null, 2014)]
    [InlineData("", 2014)]
    public void Без_года_или_названия_поиск_не_идёт(string title, int year)
    {
        Assert.False(ImdbIndex.TryGetByTitle(title, year, out _));
    }

    [Fact]
    public void Без_названия_не_запоминаем()
    {
        ImdbIndex.Remember("tt9999992", null, "Something", 2003);
        ImdbIndex.Remember("tt9999993", "", "Something", 2003);

        Assert.False(ImdbIndex.TryGet("tt9999992", out _));
        Assert.False(ImdbIndex.TryGet("tt9999993", out _));
    }
}
