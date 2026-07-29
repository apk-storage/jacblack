using JacRed.Infrastructure.Persistence;
using Xunit;

namespace JacRed.Tests.Search;

/// <summary>
/// Отбор по типу содержимого на входе в базу.
///
/// Лампа работает с TMDB, поэтому спортивные трансляции и прочее, чего
/// в TMDB нет, до человека не дойдёт. Замер 29.07.2026: в базе лежало
/// 2.5% спорта — около 27 тысяч записей мёртвым грузом.
/// </summary>
public class ContentTypeFilterTests
{
    [Theory]
    [InlineData("movie")]
    [InlineData("serial")]
    [InlineData("anime")]
    [InlineData("multfilm")]
    [InlineData("multserial")]
    [InlineData("documovie")]
    [InlineData("docuserial")]
    [InlineData("tvshow")]
    public void Видео_берём(string type)
    {
        Assert.True(FileDB.IsWantedContent(new[] { type }));
    }

    [Theory]
    [InlineData("sport")]
    [InlineData("music")]
    [InlineData("books")]
    [InlineData("software")]
    public void Того_чего_нет_в_TMDB_не_берём(string type)
    {
        Assert.False(FileDB.IsWantedContent(new[] { type }));
    }

    [Fact]
    public void Смешанный_набор_берём_если_есть_нужное()
    {
        // Раздача, помеченная и спортом, и документалкой, нам подходит:
        // в TMDB документалка есть.
        Assert.True(FileDB.IsWantedContent(new[] { "sport", "documovie" }));
    }

    [Fact]
    public void Без_типа_пропускаем()
    {
        // Часть трекеров тип не проставляет. Молча терять такие раздачи
        // хуже, чем оставить лишнее: их потом видно по выдаче.
        Assert.True(FileDB.IsWantedContent(null));
        Assert.True(FileDB.IsWantedContent(new string[0]));
    }
}
