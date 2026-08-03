using JacBlack.Infrastructure.Utils;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Совпадение запроса с названием по границе слова.
///
/// Ключ базы — склейка имени и оригинального имени без пробелов, и поиск
/// шёл по ней подстрокой. Отсюда «Веном» находил «Новый Мир с Стивеном
/// Хокингом»: в «новыймирсстиве|ном|хокингом» подстрока лежит на стыке слов.
/// Замер 31.07.2026 — 28 таких документалок в выдаче по «Веном» из 283.
///
/// Требовать целое слово нельзя: люди ищут по началу («интерстел») и ждут
/// «Интерстеллар». Поэтому совпадение обязано начинаться с границы слова,
/// а обрываться может где угодно.
/// </summary>
public class TitleMatchTests
{
    [Fact]
    public void Не_находит_внутри_другого_слова()
    {
        // Тот самый случай, ради которого всё написано.
        Assert.False(TitleMatch.StartsAtWordBoundary("Новый Мир с Стивеном Хокингом", "веном"));
        Assert.False(TitleMatch.StartsAtWordBoundary("Во Вселенную со Стивеном Хокингом", "веном"));
    }

    [Fact]
    public void Находит_по_началу_слова()
    {
        // А это должно продолжать работать: люди ищут по части слова.
        Assert.True(TitleMatch.StartsAtWordBoundary("Интерстеллар", "интерстел"));
        Assert.True(TitleMatch.StartsAtWordBoundary("Интерстеллар", "интерстеллар"));
    }

    [Fact]
    public void Находит_со_второго_и_последующих_слов()
    {
        Assert.True(TitleMatch.StartsAtWordBoundary("Веном: Последний танец", "последний"));
        Assert.True(TitleMatch.StartsAtWordBoundary("Веном: Последний танец", "танец"));
    }

    [Fact]
    public void Запрос_может_перекрывать_несколько_слов()
    {
        // Запрос приходит уже без пробелов, поэтому «последний танец»
        // выглядит как «последнийтанец» и обязан совпасть.
        Assert.True(TitleMatch.StartsAtWordBoundary("Веном: Последний танец", "последнийтанец"));
        Assert.True(TitleMatch.StartsAtWordBoundary("Веном: Последний танец", "веномпоследний"));
    }

    [Fact]
    public void Знаки_и_регистр_не_мешают()
    {
        Assert.True(TitleMatch.StartsAtWordBoundary("Dune: Part One", "part"));
        Assert.True(TitleMatch.StartsAtWordBoundary("Дюна — Часть вторая", "часть"));
        Assert.True(TitleMatch.StartsAtWordBoundary("S.W.A.T.", "swat"));
    }

    [Fact]
    public void Цифры_считаются_частью_слова()
    {
        Assert.True(TitleMatch.StartsAtWordBoundary("Веном 2: Карнаж", "веном2"));
        Assert.True(TitleMatch.StartsAtWordBoundary("Терминатор 2", "терминатор"));
    }

    [Fact]
    public void Пустой_запрос_пропускает_всё()
    {
        // Пустым запросом отбирают по типу или трекеру — резать тут нечего.
        Assert.True(TitleMatch.StartsAtWordBoundary("что угодно", ""));
        Assert.True(TitleMatch.StartsAtWordBoundary("что угодно", null));
    }

    [Fact]
    public void Пустое_название_ничему_не_соответствует()
    {
        Assert.False(TitleMatch.StartsAtWordBoundary("", "веном"));
        Assert.False(TitleMatch.StartsAtWordBoundary(null, "веном"));
    }

    [Fact]
    public void Достаточно_совпадения_с_любым_из_двух_названий()
    {
        // У русских трекеров оригинальное имя хранится отдельно, и запрос
        // на английском должен находить их по нему.
        Assert.True(TitleMatch.Matches("Веном: Последний танец", "Venom: The Last Dance", "venom", null));
        Assert.True(TitleMatch.Matches("Веном: Последний танец", "Venom: The Last Dance", "веном", null));
    }

    [Fact]
    public void Второй_запрос_проверяется_наравне_с_первым()
    {
        // Второй запрос — это подставленный перевод: «Веном» → «Venom».
        // По нему находятся англоязычные источники вроде yts.
        Assert.True(TitleMatch.Matches("Venom: The Last Dance", "Venom: The Last Dance", "веном", "venom"));
        Assert.False(TitleMatch.Matches("Новый Мир с Стивеном Хокингом", null, "веном", "venom"));
    }

    [Fact]
    public void Короткое_оригинальное_название_не_ловится_в_середине_чужих()
    {
        // Сериал «Извне» с оригинальным названием From. Это обычное английское
        // слово, и мягкое совпадение втягивало в выдачу всё, где оно есть.
        // Замер 31.07.2026: по одному русскому названию 114 верных раздач,
        // вместе с оригинальным — 1414, почти сплошь чужих.
        Assert.False(TitleMatch.Matches(
            "The Most Heretical Last Boss Queen: From Villainess to Savior",
            "The Most Heretical Last Boss Queen: From Villainess to Savior",
            "извне", "from"));

        Assert.False(TitleMatch.Matches(
            "Rakudai Kenja no Gakuin Musou: Nidome no Tensei, S-Rank Cheat",
            "Rakudai Kenja no Gakuin Musou", "извне", "from"));
    }

    [Fact]
    public void Своя_раздача_по_короткому_оригинальному_названию_находится()
    {
        // При этом настоящие раздачи сериала теряться не должны — ни те,
        // где название стоит первым, ни русские.
        Assert.True(TitleMatch.Matches("From", "From", "извне", "from"));
        Assert.True(TitleMatch.Matches("Извне", "From", "извне", "from"));
        Assert.True(TitleMatch.Matches("From: Season 4", "From", "извне", "from"));
    }

    [Fact]
    public void Свободный_запрос_остаётся_мягким()
    {
        // То, что человек печатает руками, ищется по-прежнему по любому слову:
        // строгость нужна названию карточки, а не строке поиска.
        Assert.True(TitleMatch.StartsAtWordBoundary("Веном: Последний танец", "танец"));
        Assert.True(TitleMatch.Matches("Веном: Последний танец", null, "танец", null));
    }

    [Fact]
    public void Поиск_по_карточке_строг_и_к_русскому_названию()
    {
        // Карточка «Мир» не должна тянуть «Дивный новый мир» — это разные вещи.
        Assert.False(TitleMatch.Matches("Дивный новый мир", "Brave New World", "мир", "world"));
        Assert.True(TitleMatch.Matches("Мир Дикого Запада", "Westworld", "мир", "westworld"));
    }

    [Fact]
    public void Название_карточки_должно_стоять_в_начале()
    {
        Assert.True(TitleMatch.StartsWithQuery("From S04E10", "from"));
        Assert.False(TitleMatch.StartsWithQuery("Something From Nothing", "from"));
        Assert.True(TitleMatch.StartsWithQuery("Дюна: Часть вторая", "дюна"));
    }
}
