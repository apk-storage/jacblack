using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using JacRed.Infrastructure.Logging;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Persistence
{
    /// <summary>Что мы знаем о фильме по коду IMDB.</summary>
    public class ImdbTitle
    {
        [JsonProperty("n")]
        public string Name { get; set; }

        [JsonProperty("o")]
        public string OriginalName { get; set; }

        [JsonProperty("y")]
        public int Year { get; set; }
    }

    /// <summary>
    /// Словарь «код IMDB → название».
    ///
    /// Зачем. Лампа умеет спрашивать раздачи по коду, и такой запрос до сих пор
    /// уходил в ЧУЖОЙ сервис, чтобы узнать название. Токена к нему нет, поэтому
    /// поиск по коду не работал вовсе. Но код приходит вместе с названием от
    /// eztv, yts и piratebay — значит словарь можно собрать из своей же базы и
    /// посредник больше не нужен.
    ///
    /// Почему отдельным файлом, а не обходом базы. Код есть у малой части
    /// записей, а полный обход миллиона раздач занимает под три минуты — держать
    /// его в общем индексе, который пересобирается каждые десять минут, нельзя.
    /// Словарь пополняется в момент записи и весит килобайты.
    ///
    /// Русские трекеры кода не сообщают, и это не мешает: достаточно, чтобы
    /// код принесла ХОТЬ ОДНА раздача того же фильма — дальше поиск идёт по
    /// названию и находит все остальные, включая русские.
    /// </summary>
    public static class ImdbIndex
    {
        const string Path = "Data/imdb.json";

        static readonly ConcurrentDictionary<string, ImdbTitle> _titles =
            new ConcurrentDictionary<string, ImdbTitle>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Обратный поиск: «оригинальное название + год» → код.
        ///
        /// Нужен, чтобы подтянуть код к раздачам, у которых его нет. Код
        /// сообщают три источника из двадцати, но фильм-то один и тот же:
        /// если yts принёс «Interstellar 2014 → tt0816692», то и русская
        /// раздача с тем же оригинальным названием и годом — про него же.
        ///
        /// Год в ключе обязателен: без него «Дюна» 1984 года склеилась бы
        /// с «Дюной» 2021-го.
        /// </summary>
        static readonly ConcurrentDictionary<string, string> _byTitle =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        static int _dirty;
        static int _loaded;

        public static int Count => _titles.Count;

        public static void Load()
        {
            if (Interlocked.Exchange(ref _loaded, 1) == 1)
                return;

            try
            {
                if (!File.Exists(Path))
                    return;

                var data = JsonConvert.DeserializeObject<Dictionary<string, ImdbTitle>>(File.ReadAllText(Path));
                if (data == null)
                    return;

                foreach (var kv in data)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null)
                        continue;

                    _titles[kv.Key] = kv.Value;
                    RememberTitleKey(kv.Key, kv.Value.OriginalName, kv.Value.Year);
                    RememberTitleKey(kv.Key, kv.Value.Name, kv.Value.Year);
                }

                JacRedLog.Information(JacRedLogCategories.Fdb, $"словарь кодов IMDB загружен: {_titles.Count}");
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Fdb, "словарь кодов IMDB не загрузился", ex);
            }
        }

        /// <summary>
        /// Запомнить связку. Вызывается при записи раздачи, у которой источник
        /// сообщил код. Уже известное не перезаписываем: первый источник обычно
        /// не хуже следующего, а лишние записи на диск ни к чему.
        /// </summary>
        public static void Remember(string imdb, string name, string originalname, int year)
        {
            if (string.IsNullOrWhiteSpace(imdb) || string.IsNullOrWhiteSpace(name))
                return;

            var title = new ImdbTitle { Name = name, OriginalName = originalname, Year = year };

            if (_titles.TryAdd(imdb, title))
                Interlocked.Exchange(ref _dirty, 1);

            RememberTitleKey(imdb, originalname, year);
            RememberTitleKey(imdb, name, year);
        }

        static void RememberTitleKey(string imdb, string title, int year)
        {
            string key = TitleKey(title, year);
            if (key != null)
                _byTitle.TryAdd(key, imdb);
        }

        /// <summary>
        /// Ключ обратного поиска. Название приводится тем же способом, что и
        /// ключи базы, — без пробелов, знаков и регистра, — чтобы «Dune: Part
        /// One» и «Dune Part One» считались одним и тем же.
        /// </summary>
        static string TitleKey(string title, int year)
        {
            if (string.IsNullOrWhiteSpace(title) || year <= 1900)
                return null;

            string normalized = Utils.StringConvert.SearchName(title);
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized + ":" + year;
        }

        /// <summary>
        /// Найти код по названию и году. Так код подтягивается к раздачам
        /// источников, которые его не сообщают.
        /// </summary>
        public static bool TryGetByTitle(string title, int year, out string imdb)
        {
            imdb = null;
            string key = TitleKey(title, year);
            return key != null && _byTitle.TryGetValue(key, out imdb);
        }

        public static bool TryGet(string imdb, out ImdbTitle title)
        {
            title = null;
            return !string.IsNullOrWhiteSpace(imdb) && _titles.TryGetValue(imdb, out title);
        }

        /// <summary>Сохраняет, только если что-то добавилось с прошлого раза.</summary>
        public static void SaveIfDirty()
        {
            if (Interlocked.Exchange(ref _dirty, 0) == 0)
                return;

            try
            {
                var snapshot = new Dictionary<string, ImdbTitle>(_titles, StringComparer.OrdinalIgnoreCase);

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));

                // Обычным JSON, а не сжатым: словарь маленький, и его полезно
                // уметь открыть глазами. Пишем через временный файл, чтобы
                // обрыв на середине не оставил половину словаря.
                string temp = Path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
                File.Move(temp, Path, overwrite: true);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _dirty, 1);
                JacRedLog.Swallowed(JacRedLogCategories.Fdb, "словарь кодов IMDB не сохранился", ex);
            }
        }
    }
}
