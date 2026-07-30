using JacRed.Infrastructure.Persistence;
using Xunit;

namespace JacRed.Tests.Search;

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
    public void Без_названия_не_запоминаем()
    {
        ImdbIndex.Remember("tt9999992", null, "Something", 2003);
        ImdbIndex.Remember("tt9999993", "", "Something", 2003);

        Assert.False(ImdbIndex.TryGet("tt9999992", out _));
        Assert.False(ImdbIndex.TryGet("tt9999993", out _));
    }
}
