using System.Collections.Generic;
using System.Linq;
using JacBlack.Application.Search;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Потолок на чтение файлов делится между двумя названиями карточки. Раньше
/// оба набора ключей сваливались в один HashSet и обрезались одним `Take` —
/// порядок обхода не определён, поэтому пропадала редкая сторона. Били по
/// зарубежным источникам: у раздачи с piratebay или yts русского названия нет,
/// и лежит она только под ключом оригинального.
///
/// Замер 07.08.2026, карточка «Дюна / Dune» 2021: по русскому 117 раздач, по
/// оригинальному 125 (из них 8 зарубежных), вместе — 60 и ни одной зарубежной.
/// </summary>
public class CardKeyBudgetTests
{
    static List<string> Keys(string prefix, int n) =>
        Enumerable.Range(0, n).Select(i => $"{prefix}{i}").ToList();

    [Fact]
    public void Меньший_набор_проходит_целиком_если_влезает_в_бронь()
    {
        // Обычный случай: зарубежных раздач у карточки заметно меньше, и они
        // укладываются в забронированную половину — значит не теряется ни одна.
        var ru = Keys("ru", 400);
        var en = Keys("en", 40);

        var got = CardKeyBudget.Split(ru, en, cap: 100);

        Assert.Equal(100, got.Count);
        Assert.All(en, k => Assert.Contains(k, got));
    }

    [Fact]
    public void Меньший_набор_больше_брони_получает_ровно_половину()
    {
        // Бронь — половина потолка, не больше: иначе редкая сторона объела бы
        // частую. 60 ключей при потолке 100 целиком не влезают, и это верно.
        var got = CardKeyBudget.Split(Keys("ru", 400), Keys("en", 60), cap: 100);

        Assert.Equal(50, got.Count(k => k.StartsWith("en")));
        Assert.Equal(50, got.Count(k => k.StartsWith("ru")));
    }

    [Fact]
    public void Потолок_не_превышается()
    {
        var got = CardKeyBudget.Split(Keys("ru", 900), Keys("en", 800), cap: 500);

        Assert.Equal(500, got.Count);
    }

    [Fact]
    public void Оба_набора_велики_каждому_достаётся_половина()
    {
        var got = CardKeyBudget.Split(Keys("ru", 900), Keys("en", 800), cap: 500);

        // Меньшему (en) забронировано ровно cap/2, остальное добирает больший.
        Assert.Equal(250, got.Count(k => k.StartsWith("en")));
        Assert.Equal(250, got.Count(k => k.StartsWith("ru")));
    }

    [Fact]
    public void Один_набор_пуст_второй_добирает_весь_потолок()
    {
        var got = CardKeyBudget.Split(Keys("ru", 300), null, cap: 100);

        Assert.Equal(100, got.Count);
        Assert.All(got, k => Assert.StartsWith("ru", k));
    }

    [Fact]
    public void Совпадающие_ключи_не_считаются_дважды()
    {
        // Одна и та же раздача попадает в оба набора, если у неё совпадают
        // русское и оригинальное названия (русское кино). Потолок при этом
        // должен выбираться полностью, а не наполовину.
        var общие = Keys("both", 40);

        var got = CardKeyBudget.Split(общие, общие, cap: 100);

        Assert.Equal(40, got.Count);
    }

    [Fact]
    public void Нулевой_потолок_ничего_не_читает()
    {
        Assert.Empty(CardKeyBudget.Split(Keys("ru", 10), Keys("en", 10), cap: 0));
    }
}
