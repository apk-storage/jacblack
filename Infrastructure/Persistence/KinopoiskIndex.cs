using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using JacBlack.Infrastructure.Logging;
using Newtonsoft.Json;

namespace JacBlack.Infrastructure.Persistence
{
    /// <summary>Что мы знаем о фильме по коду Кинопоиска.</summary>
    public class KinopoiskTitle
    {
        [JsonProperty("n")]
        public string Name { get; set; }

        [JsonProperty("o")]
        public string OriginalName { get; set; }

        [JsonProperty("y")]
        public int Year { get; set; }
    }

    /// <summary>
    /// Словарь «код Кинопоиска → название». Близнец <see cref="ImdbIndex"/>,
    /// но для другой половины мира.
    ///
    /// Зачем отдельный. Код IMDB разводит тёзок только у зарубежных вещей:
    /// словарь кодов собран из англоязычных источников (yts, eztv, piratebay),
    /// а у русского кино кода IMDB нет ни в одной нашей базе — и Лампа код в
    /// торрент-поиск не шлёт вовсе. Между тем именно на русских названиях
    /// тёзки и болят: «Русская жена» без года ловит подстрокой посторонние
    /// раздачи. Кинопоиск закрывает ровно этот случай, а ссылку на него
    /// публикует kinozal.
    ///
    /// Чего здесь намеренно НЕТ, в отличие от словаря IMDB:
    ///
    /// • списка всех написаний (Aka) и перевода запроса — они нужны, чтобы
    ///   находить русскую вещь по английскому названию и наоборот. У русского
    ///   кино эта беда не стоит: и трекеры, и карточка называют его по-русски;
    /// • перечисления всех названий — оно нужно обходу piratebay, который к
    ///   русскому кино отношения не имеет.
    ///
    /// Меньше кода — меньше мест, где он может соврать. Понадобится — добавим,
    /// устройство то же.
    /// </summary>
    public static class KinopoiskIndex
    {
        const string Path = "Data/kinopoisk.json";

        static readonly ConcurrentDictionary<string, KinopoiskTitle> _titles =
            new ConcurrentDictionary<string, KinopoiskTitle>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Обратный поиск «название + год → код». Год в ключе обязателен: без
        /// него «Ирония судьбы» 1975 года склеилась бы с продолжением 2007-го.
        /// </summary>
        static readonly ConcurrentDictionary<string, string> _byTitle =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        static int _dirty;
        static int _loaded;

        /// <summary>Когда словарь записывали в последний раз, в тиках UTC.</summary>
        static long _lastSave;

        static readonly TimeSpan MinSaveInterval = TimeSpan.FromMinutes(5);

        static readonly IndexWriteGuard _guard = new IndexWriteGuard("словарь кодов Кинопоиска");

        public static int Count => _titles.Count;

        public static void Load()
        {
            if (Interlocked.Exchange(ref _loaded, 1) == 1)
                return;

            try
            {
                if (!File.Exists(Path))
                {
                    _guard.FileMissing();
                    return;
                }

                // Потоком, а не через ReadAllText: словарь IMDB на этом обжёгся,
                // когда дорос до 45 МБ и целиком уходил в кучу больших объектов.
                Dictionary<string, KinopoiskTitle> data;
                using (var reader = new StreamReader(Path))
                using (var json = new JsonTextReader(reader))
                    data = new JsonSerializer().Deserialize<Dictionary<string, KinopoiskTitle>>(json);

                if (data == null)
                {
                    // Файл есть, а содержимого нет — это поломка, а не пустой
                    // словарь. Писать поверх нельзя.
                    _guard.LoadFailed("файл прочитан как пустой");
                    return;
                }

                foreach (var kv in data)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null)
                        continue;

                    _titles[kv.Key] = kv.Value;
                    RememberTitleKey(kv.Key, kv.Value.OriginalName, kv.Value.Year);
                    RememberTitleKey(kv.Key, kv.Value.Name, kv.Value.Year);
                }

                _guard.LoadSucceeded(_titles.Count);

                JacBlackLog.Information(JacBlackLogCategories.Fdb,
                    $"словарь кодов Кинопоиска загружен: {_titles.Count}");
            }
            catch (Exception ex)
            {
                _guard.LoadFailed(ex.Message);
                JacBlackLog.Swallowed(JacBlackLogCategories.Fdb, "словарь кодов Кинопоиска не загрузился", ex);
            }
        }

        /// <summary>
        /// Запомнить связку. Пара «код → название» не перезаписывается: первый
        /// источник не хуже следующего, а код у вещи один.
        /// </summary>
        public static void Remember(string kinopoisk, string name, string originalname, int year)
        {
            if (string.IsNullOrWhiteSpace(kinopoisk) || string.IsNullOrWhiteSpace(name))
                return;

            var title = new KinopoiskTitle { Name = name, OriginalName = originalname, Year = year };

            if (_titles.TryAdd(kinopoisk, title))
                Interlocked.Exchange(ref _dirty, 1);

            RememberTitleKey(kinopoisk, originalname, year);
            RememberTitleKey(kinopoisk, name, year);
        }

        static void RememberTitleKey(string kinopoisk, string title, int year)
        {
            string key = TitleKey(title, year);
            if (key != null)
                _byTitle.TryAdd(key, kinopoisk);
        }

        /// <summary>
        /// Ключ обратного поиска. Название приводится тем же способом, что и
        /// ключи базы, — без пробелов, знаков и регистра.
        /// </summary>
        static string TitleKey(string title, int year)
        {
            if (string.IsNullOrWhiteSpace(title) || year <= 1900)
                return null;

            string normalized = Utils.StringConvert.SearchName(title);
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized + ":" + year;
        }

        /// <summary>Найти код по названию и году.</summary>
        public static bool TryGetByTitle(string title, int year, out string kinopoisk)
        {
            kinopoisk = null;
            string key = TitleKey(title, year);
            return key != null && _byTitle.TryGetValue(key, out kinopoisk);
        }

        public static bool TryGet(string kinopoisk, out KinopoiskTitle title)
        {
            title = null;
            return !string.IsNullOrWhiteSpace(kinopoisk) && _titles.TryGetValue(kinopoisk, out title);
        }

        /// <summary>
        /// Сохраняет, только если что-то добавилось, и не чаще раза в пять
        /// минут. Ограничение по времени — наследство словаря IMDB: сборщик
        /// кодов звал сохранение после каждой добытой строчки, и на выросшем
        /// файле это превращалось в десятки мегабайт записи на одну связку.
        ///
        /// <paramref name="force"/> — для миграций и остановки, когда потерять
        /// накопленное нельзя.
        /// </summary>
        public static void SaveIfDirty(bool force = false)
        {
            if (Volatile.Read(ref _dirty) == 0)
                return;

            if (!force)
            {
                long now = DateTime.UtcNow.Ticks;
                long last = Interlocked.Read(ref _lastSave);

                if (now - last < MinSaveInterval.Ticks)
                    return;

                // Отметку времени занимаем до записи: иначе два потока,
                // подошедшие разом, оба решат, что пора, и запишут дважды.
                if (Interlocked.CompareExchange(ref _lastSave, now, last) != last)
                    return;
            }
            else
            {
                Interlocked.Exchange(ref _lastSave, DateTime.UtcNow.Ticks);
            }

            if (Interlocked.Exchange(ref _dirty, 0) == 0)
                return;

            try
            {
                var snapshot = new Dictionary<string, KinopoiskTitle>(_titles, StringComparer.OrdinalIgnoreCase);

                if (!_guard.MayWrite(snapshot.Count))
                {
                    // Отметку не гасим: как только словарь снова наполнится,
                    // сохранение должно состояться.
                    Interlocked.Exchange(ref _dirty, 1);
                    return;
                }

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));

                // Через временный файл, чтобы обрыв на середине не оставил
                // половину словаря.
                string temp = Path + ".tmp";

                using (var writer = new StreamWriter(temp))
                    new JsonSerializer().Serialize(writer, snapshot);

                File.Move(temp, Path, overwrite: true);
                _guard.WriteSucceeded(snapshot.Count);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _dirty, 1);
                JacBlackLog.Swallowed(JacBlackLogCategories.Fdb, "словарь кодов Кинопоиска не сохранился", ex);
            }
        }
    }
}
