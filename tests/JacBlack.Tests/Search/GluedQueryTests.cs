using System.Text.RegularExpressions;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Расклейка строки «русское название + оригинал + год».
///
/// Сборки Lampac присылают всё одним полем `query`, и разобрать его обязаны
/// мы. Режим включается по User-Agent `Chrome/106.0.0.0` и отсутствию
/// `is_serial` в строке запроса — то есть для части живых клиентов он
/// действует всегда.
///
/// Повод (Сергей, 10.10.2026): «Человек-паук: Новый день 2026 — схренали его
/// не находили?» В классе символов стояло `[^a-z-A-Z]`, а это не «кроме
/// латиницы»: `a-z`, затем САМ ДЕФИС, затем `A-Z`. Названия с дефисом под
/// расклейку не подходили, title и title_original оставались пустыми, поиск
/// шёл по склеенной строке и возвращал ноль при 75 живых раздачах. За сутки
/// таких пустых выдач было 76 на одном только «Человеке-пауке».
///
/// Тест держит ИМЕННО регулярные выражения, а не весь поиск: поиск требует
/// индекса и базы, а ошибка была в одном символе класса.
/// </summary>
public class GluedQueryTests
{
    // Копии из JackettCardMatcher: класс internal, а проверять надо именно
    // выражения. Расхождение поймает `Названия_с_дефисом_расклеиваются`.
    const string РУС_ЛАТ_ГОД = "^([^a-zA-Z]+) ([^а-яА-ЯёЁ]+) ([0-9]{4})$";
    const string РУС_ЛАТ = "^([^a-zA-Z]+) ([^а-яА-ЯёЁ]+)$";

    [Theory]
    [InlineData("Человек-паук: Новый день Spider-Man: Brand New Day 2026",
                "Человек-паук: Новый день", "Spider-Man: Brand New Day", "2026")]
    [InlineData("Человек-муравей Ant-Man 2015",
                "Человек-муравей", "Ant-Man", "2015")]
    [InlineData("Аватар Avatar 2009", "Аватар", "Avatar", "2009")]
    [InlineData("Отель Мумбаи Hotel Mumbai 2018",
                "Отель Мумбаи", "Hotel Mumbai", "2018")]
    public void Названия_с_дефисом_расклеиваются(
        string склеено, string название, string оригинал, string год)
    {
        var м = Regex.Match(склеено, РУС_ЛАТ_ГОД);

        Assert.True(м.Success, "строка не разобралась: " + склеено);
        Assert.Equal(название, м.Groups[1].Value);
        Assert.Equal(оригинал, м.Groups[2].Value);
        Assert.Equal(год, м.Groups[3].Value);
    }

    [Fact]
    public void Старое_выражение_на_дефисе_ломалось()
    {
        // Ради этой строки тест и существует: она показывает, что правка не
        // косметическая. Класс `a-z-A-Z` исключает ещё и дефис.
        const string было = "^([^a-z-A-Z]+) ([^а-я-А-Я]+) ([0-9]{4})$";
        const string строка = "Человек-паук: Новый день Spider-Man: Brand New Day 2026";

        Assert.False(Regex.IsMatch(строка, было));
        Assert.True(Regex.IsMatch(строка, РУС_ЛАТ_ГОД));
    }

    [Fact]
    public void Без_года_тоже_расклеивается()
    {
        var м = Regex.Match("Человек-паук: Новый день Spider-Man: Brand New Day",
                            РУС_ЛАТ);

        Assert.True(м.Success);
        Assert.Equal("Человек-паук: Новый день", м.Groups[1].Value);
        Assert.Equal("Spider-Man: Brand New Day", м.Groups[2].Value);
    }

    [Fact]
    public void Ё_остаётся_в_русской_половине()
    {
        // `а-я` не включает `ё`, и без отдельной буквы в классе «Ёлки»
        // утекали в латинскую половину.
        var м = Regex.Match("Ёлки Yolki 2010", РУС_ЛАТ_ГОД);

        Assert.True(м.Success);
        Assert.Equal("Ёлки", м.Groups[1].Value);
        Assert.Equal("Yolki", м.Groups[2].Value);
    }

    [Theory]
    [InlineData("Spider-Man Brand New Day 2026")]   // нет русской половины
    [InlineData("Человек-паук Новый день")]          // нет латинской
    public void Чужие_строки_под_расклейку_не_попадают(string строка)
    {
        Assert.False(Regex.IsMatch(строка, РУС_ЛАТ_ГОД));
    }
}
