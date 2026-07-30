using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JacRed.Infrastructure.Trackers.Rutor;
using JacRed.Models.Details;
using Newtonsoft.Json;
using Xunit;

namespace JacRed.Tests.Rutor;

/// <summary>
/// Эталонный снимок выдачи парсера: не «сколько раздач нашлось», а что именно
/// в каждой из них — до последнего поля.
///
/// Зачем это нужно отдельно от RutorParserFixtureTests: тот проверяет, что
/// разбор в принципе работает (не меньше сорока раздач, поля непустые). Такая
/// проверка пройдёт и после переписывания парсера, которое незаметно потеряло
/// год выпуска или сбило originalname у половины записей. Здесь же любое
/// расхождение видно построчно.
///
/// Пересоздать эталоны (только осознанно, когда разметка трекера поменялась
/// и новый результат проверен глазами):
///   JACRED_UPDATE_GOLDEN=1 dotnet test --filter RutorParserGoldenTests
/// </summary>
public class RutorParserGoldenTests
{
    public static IEnumerable<object[]> Categories() =>
        RutorCategories.Map.Keys.OrderBy(int.Parse).Select(cat => new object[] { cat });

    [Theory]
    [MemberData(nameof(Categories))]
    public void ParseTorrentsFromPage_СовпадаетСЭталоном(string cat)
    {
        string html = FixtureLoader.Read($"Rutor/browse_{cat}.html");
        var parsed = RutorParser.ParseTorrentsFromPage(html, cat);

        string actual = Serialize(parsed);
        string goldenPath = GoldenPath(cat);

        if (Environment.GetEnvironmentVariable("JACRED_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath));
            File.WriteAllText(goldenPath, actual);
            return;
        }

        Assert.True(File.Exists(goldenPath),
            $"нет эталона {goldenPath} — создать: JACRED_UPDATE_GOLDEN=1 dotnet test --filter RutorParserGoldenTests");

        string expected = File.ReadAllText(goldenPath);

        // Сравниваем построчно: при расхождении xunit покажет конкретную запись,
        // а не «две строки на 200 КБ не равны».
        var exp = expected.Replace("\r\n", "\n").Split('\n');
        var act = actual.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < Math.Min(exp.Length, act.Length); i++)
            Assert.Equal(exp[i], act[i]);

        Assert.Equal(exp.Length, act.Length);
    }

    static string GoldenPath(string cat) =>
        Path.Combine(FixtureLoader.FixturesRoot, "Rutor", "Golden", $"browse_{cat}.json");

    /// <summary>
    /// Только те поля, что парсер действительно заполняет. Порядок записей
    /// сохраняем как есть — он тоже часть поведения: по нему идёт дедупликация.
    /// </summary>
    static string Serialize(List<TorrentBaseDetails> torrents) =>
        JsonConvert.SerializeObject(torrents.Select(t => new
        {
            t.trackerName,
            t.types,
            t.url,
            t.title,
            t.sid,
            t.pir,
            t.sizeName,
            t.magnet,
            createTime = t.createTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            t.name,
            t.originalname,
            t.relased
        }), Formatting.Indented);
}
