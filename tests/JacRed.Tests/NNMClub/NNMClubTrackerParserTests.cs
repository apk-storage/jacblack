using System;
using System.Linq;
using JacRed.Infrastructure.Trackers.NNMClub;
using Xunit;

namespace JacRed.Tests.NNMClub;

/// <summary>
/// Разбор выдачи tracker.php — второй точки входа, через которую берётся архив.
///
/// Портал для архива непригоден: у nnmclub потолок в 200 результатов на любой
/// запрос, поэтому portal.php со start=1000 отдаёт перенаправление на
/// тему-заглушку. Обход дробится по форумам, а строки читаются отсюда.
/// </summary>
public class NNMClubTrackerParserTests
{
    static System.Collections.Generic.List<NNMClubTrackerParser.Row> Rows()
        => NNMClubTrackerParser.Parse(FixtureLoader.Read("NNMClub/tracker-f218.html"));

    [Fact]
    public void Страница_отдаёт_полсотни_строк()
    {
        // Ровно PageSize: выдача листается по 50, и потолок в 200 набирается
        // четырьмя такими страницами.
        Assert.Equal(NNMClubTrackerParser.PageSize, Rows().Count);
    }

    [Fact]
    public void У_каждой_строки_есть_тема_заголовок_и_ссылка_на_файл()
    {
        Assert.All(Rows(), r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.TopicId));
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.False(string.IsNullOrWhiteSpace(r.DownloadId));
        });
    }

    [Fact]
    public void Размер_читается_и_числом_и_строкой()
    {
        var rows = Rows();

        Assert.All(rows, r => Assert.True(r.SizeBytes > 0, $"нет размера у «{r.Title}»"));
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.SizeName), $"нет строки размера у «{r.Title}»"));
    }

    [Fact]
    public void Дата_берётся_из_unix_метки_а_не_из_размера()
    {
        // Мина, на которой я едва не подорвался: размер тоже лежит в теге u и
        // тоже бывает десятизначным. Раздача около гигабайта прочиталась бы как
        // дата 2001 года, поэтому дату берём из ячейки «Торрент-файл добавлен».
        var rows = Rows();

        Assert.All(rows, r => Assert.NotEqual(default, r.CreateTime));
        Assert.All(rows, r => Assert.True(r.CreateTime > new DateTime(2015, 1, 1), $"дата слишком старая у «{r.Title}»: {r.CreateTime}"));
        Assert.All(rows, r => Assert.True(r.CreateTime < DateTime.UtcNow.AddDays(2), $"дата из будущего у «{r.Title}»: {r.CreateTime}"));
    }

    [Fact]
    public void Сиды_и_личи_читаются()
    {
        var rows = Rows();

        // Не требуем ненулевых у каждой строки: ноль сидов — законное значение.
        // Проверяем, что счётчики вообще извлекаются хоть у кого-то, иначе
        // молча получили бы нули у всей выдачи, как когда-то у megapeer.
        Assert.Contains(rows, r => r.Sid > 0);
        Assert.Contains(rows, r => r.Pir > 0);
    }

    [Fact]
    public void Имя_форума_есть_и_ведёт_к_разделу()
    {
        var rows = Rows();

        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.ForumName)));

        var (types, _) = NNMClubCategories.ForForumName(rows[0].ForumName);
        Assert.NotEmpty(types);
    }

    [Theory]
    [InlineData("Аниме", "anime")]
    [InlineData("Документальные фильмы", "documovie")]
    [InlineData("Зарубежные сериалы", "serial")]
    [InlineData("Спорт и активный отдых", "sport")]
    [InlineData("Зарубежные Новинки (SD, DVD)", "movie")]
    public void Раздел_определяется_по_названию_форума(string forum, string expected)
    {
        var (types, _) = NNMClubCategories.ForForumName(forum);
        Assert.Contains(expected, types);
    }

    [Fact]
    public void Пустая_страница_не_роняет_разбор()
    {
        Assert.Empty(NNMClubTrackerParser.Parse(null));
        Assert.Empty(NNMClubTrackerParser.Parse(string.Empty));
        Assert.Empty(NNMClubTrackerParser.Parse("<html><body>ничего</body></html>"));
    }
}
