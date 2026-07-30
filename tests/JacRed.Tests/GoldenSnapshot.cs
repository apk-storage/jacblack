using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JacRed.Models.Details;
using Newtonsoft.Json;
using Xunit;

namespace JacRed.Tests;

/// <summary>
/// Эталонный снимок выдачи парсера: не «сколько раздач нашлось», а что именно
/// в каждой из них — до последнего поля.
///
/// Зачем отдельно от обычных тестов на снимках страниц: те проверяют, что разбор
/// в принципе работает (не меньше N раздач, поля непустые). Такая проверка
/// пройдёт и после переписывания, которое незаметно потеряло год выпуска или
/// сбило originalname у половины записей. Здесь же расхождение видно построчно.
///
/// Порядок применения при переписывании парсера — именно такой:
///   1. снять эталон СТАРЫМ парсером (JACRED_UPDATE_GOLDEN=1) и закоммитить;
///   2. переписать парсер;
///   3. прогнать тест — он и есть доказательство равносильности.
///
/// Пересоздавать эталоны можно только осознанно: когда разметка трекера
/// действительно поменялась и новый результат проверен глазами.
///   JACRED_UPDATE_GOLDEN=1 dotnet test --filter Golden
/// </summary>
public static class GoldenSnapshot
{
    /// <param name="tracker">Каталог снимков, например "Rutracker".</param>
    /// <param name="caseName">Имя случая, обычно идентификатор раздела.</param>
    public static void Assert<T>(string tracker, string caseName, List<T> parsed) where T : TorrentBaseDetails
    {
        string actual = Serialize(parsed);
        string path = Path.Combine(FixtureLoader.FixturesRoot, tracker, "Golden", $"{caseName}.json");

        if (Environment.GetEnvironmentVariable("JACRED_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, actual);
            return;
        }

        Xunit.Assert.True(File.Exists(path),
            $"нет эталона {path} — снять: JACRED_UPDATE_GOLDEN=1 dotnet test --filter Golden");

        var expected = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
        var got = actual.Replace("\r\n", "\n").Split('\n');

        // Построчно: при расхождении xunit покажет конкретную запись,
        // а не «две строки по 200 КБ не равны».
        for (int i = 0; i < Math.Min(expected.Length, got.Length); i++)
            Xunit.Assert.Equal(expected[i], got[i]);

        Xunit.Assert.Equal(expected.Length, got.Length);
    }

    /// <summary>
    /// Только те поля, что заполняют парсеры. Порядок записей сохраняем как есть —
    /// он тоже часть поведения: по нему идёт отбор дублей.
    /// </summary>
    static string Serialize<T>(List<T> torrents) where T : TorrentBaseDetails =>
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
