using JacBlack.Infrastructure.Parsing;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Разбор строки размера в байты. Раньше таких реализаций было три, и две из
/// них требовали между числом и единицей ОБЫЧНЫЙ пробел — из-за чего у toloka,
/// которая отдаёт неразрывный, размер не разбирался у 45% записей.
/// </summary>
public class SizeParserTests
{
    [Theory]
    [InlineData("700 MB", 734003200L)]
    [InlineData("1 GB", 1073741824L)]
    [InlineData("2 TB", 2199023255552L)]
    [InlineData("512 KB", 524288L)]
    [InlineData("5.09 ГБ", 5465345884L)]
    [InlineData("1,5 GB", 1610612736L)]
    public void Обычные_размеры(string text, long expected)
    {
        Assert.Equal(expected, SizeParser.ToBytes(text));
    }

    [Fact]
    public void Неразрывный_пробел_разбирается_как_обычный()
    {
        // Ровно этот случай ломался у toloka: «5.62 GB».
        Assert.Equal(SizeParser.ToBytes("5.62 GB"), SizeParser.ToBytes("5.62 GB"));
        Assert.True(SizeParser.ToBytes("5.62 GB") > 0);
    }

    [Fact]
    public void Без_пробела_тоже()
    {
        Assert.Equal(SizeParser.ToBytes("700 MB"), SizeParser.ToBytes("700MB"));
    }

    [Fact]
    public void Без_единицы_считаем_мегабайтами()
    {
        // Так вели себя обе реализации, работавшие с базой, и такие значения
        // там уже лежат — менять нельзя.
        Assert.Equal(700L * 1024 * 1024, SizeParser.ToBytes("700"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("неизвестно")]
    [InlineData("0 GB")]
    public void Что_разобрать_нельзя_даёт_ноль(string text)
    {
        Assert.Equal(0, SizeParser.ToBytes(text));
    }
}
