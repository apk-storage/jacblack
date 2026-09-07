using JacBlack.Infrastructure.Trackers.Toloka;
using Xunit;

namespace JacBlack.Tests.Toloka;

/// <summary>
/// Живые счётчики со страницы поиска toloka и — отдельно — повод жаловаться
/// на разметку.
///
/// Зачем разделять. 07.09.2026 в журнале шесть раз за сутки стояло
/// «поиск разобран в ноль — проверить разметку страницы поиска», причём по
/// запросам «Джек Ричер», «Число зверя» и «呪術廻戦». Разметка была исправна:
/// в тот же час поиск отдавал 39 раздач toloka с живыми числами (78, 65, 58
/// раздающих), а обычный обход обновлял по 720 записей за проход. На
/// украинском трекере просто не нашлось русских и японских названий.
///
/// Ложная тревога дороже, чем кажется: она приучает пропускать это сообщение
/// мимо глаз, и настоящая смена разметки — та, из-за которой 01.08.2026
/// страница в 141 КБ разбиралась в ноль, — потеряется среди неё.
/// </summary>
public class TolokaSearchSeedersTests
{
    const string RowWithCounters = @"
      <tr class='prow1'>
        <td><a class='genmed' href='t690003'>Дюна: Частина друга</a></td>
        <td><span class='seedmed'><b>78</b></span></td>
        <td><span class='leechmed'>4</span></td>
      </tr>";

    // Так выглядит страница, на которой ничего не нашлось: шапка, меню,
    // подвал — строки таблиц есть, а раздач нет ни одной.
    const string NothingFound = @"
      <table><tr><td>Пошук</td></tr>
             <tr><td>Зареєструватися</td></tr>
             <tr><td>Вхід</td></tr></table>";

    [Fact]
    public void Счётчики_со_страницы_поиска_разбираются()
    {
        var found = TolokaSyncService.ParseSearchSeeders("<table>" + RowWithCounters + "</table>");

        Assert.Single(found);
        Assert.Equal((78, 4), found["690003"]);
    }

    [Fact]
    public void Длинная_форма_адреса_тоже_принимается()
    {
        // Под ней в базе лежат старые записи.
        string row = RowWithCounters.Replace("href='t690003'", "href='/viewtopic.php?t=690003'");

        var found = TolokaSyncService.ParseSearchSeeders("<table>" + row + "</table>");

        Assert.Equal((78, 4), found["690003"]);
    }

    [Fact]
    public void На_странице_без_раздач_счётчиков_ноль()
    {
        // Это и есть признак «ничего не нашлось»: жаловаться не на что.
        Assert.Empty(TolokaSyncService.ParseSearchSeeders(NothingFound));
        Assert.Equal(0, TolokaSyncService.CountSearchSeedCounters(NothingFound));
    }

    [Fact]
    public void Счётчики_есть_а_разбор_пуст_это_повод_жаловаться()
    {
        // Настоящая смена разметки: числа на странице стоят, а ссылки на
        // раздачу в строке нет — разобрать нечего.
        const string changed = @"
          <table><tr class='prow1'>
            <td>Дюна</td><td><span class='seedmed'><b>78</b></span></td>
          </tr></table>";

        Assert.Empty(TolokaSyncService.ParseSearchSeeders(changed));
        Assert.True(TolokaSyncService.CountSearchSeedCounters(changed) > 0);
    }

    [Fact]
    public void Пустой_ответ_никого_не_роняет()
    {
        Assert.Empty(TolokaSyncService.ParseSearchSeeders(null));
        Assert.Empty(TolokaSyncService.ParseSearchSeeders(""));
        Assert.Equal(0, TolokaSyncService.CountSearchSeedCounters(null));
    }
}
