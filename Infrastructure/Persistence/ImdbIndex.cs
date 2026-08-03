using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using JacBlack.Infrastructure.Logging;
using Newtonsoft.Json;

namespace JacBlack.Infrastructure.Persistence
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

        /// <summary>
        /// Все написания названия, встреченные под этим кодом: русское,
        /// оригинальное, украинское. Отсюда строится перевод запроса.
        /// Поле необязательное — словари, записанные до его появления,
        /// читаются как есть и пополняются при первом же обходе.
        /// </summary>
        [JsonProperty("a", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Aka { get; set; }
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

        /// <summary>
        /// «Приведённое название → все написания того же фильма». Список общий
        /// с записью в <see cref="_titles"/>, поэтому пополняется сам.
        /// </summary>
        static readonly ConcurrentDictionary<string, List<string>> _akaByTitle =
            new ConcurrentDictionary<string, List<string>>(StringComparer.Ordinal);

        static int _dirty;
        static int _loaded;

        /// <summary>Когда словарь записывали в последний раз, в тиках UTC.</summary>
        static long _lastSave;

        static readonly TimeSpan MinSaveInterval = TimeSpan.FromMinutes(5);

        public static int Count => _titles.Count;

        /// <summary>
        /// Все известные оригинальные названия с годами, от новых к старым.
        ///
        /// Нужен обходу piratebay: его API не умеет постраничность — проверено
        /// 03.08.2026, `page=1` и `page=2` отдают одно и то же, — зато на поиск
        /// по названию отвечает сотней раздач.
        ///
        /// Названия берём и оригинальное, и основное: у фильма не на английском
        /// они расходятся («Le Fabuleux Destin d'Amélie Poulain» против
        /// «Amélie»), и на TPB он лежит под тем, что покороче.
        ///
        /// От новых к старым потому, что свежие релизы и ищут чаще, и на TPB
        /// их заметно больше.
        /// </summary>
        public static IReadOnlyList<(string Title, int Year)> AllTitles()
        {
            var list = new List<(string Title, int Year)>(_titles.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in _titles.Values)
            {
                if (t == null)
                    continue;

                foreach (string candidate in new[] { t.OriginalName, t.Name })
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                        continue;

                    string name = candidate.Trim();
                    if (name.Length < 2)
                        continue;

                    if (seen.Add(name))
                        list.Add((name, t.Year));
                }
            }

            list.Sort((a, b) => b.Year.CompareTo(a.Year));
            return list;
        }

        public static void Load()
        {
            if (Interlocked.Exchange(ref _loaded, 1) == 1)
                return;

            try
            {
                if (!File.Exists(Path))
                    return;

                // Потоком, а не через ReadAllText: файл вырос до 45 МБ, и целая
                // строка такого размера уходит в кучу больших объектов, откуда
                // её сборщик мусора не выселит до перезапуска.
                Dictionary<string, ImdbTitle> data;
                using (var reader = new StreamReader(Path))
                using (var json = new JsonTextReader(reader))
                    data = new JsonSerializer().Deserialize<Dictionary<string, ImdbTitle>>(json);

                if (data == null)
                    return;

                foreach (var kv in data)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null)
                        continue;

                    _titles[kv.Key] = kv.Value;
                    RememberTitleKey(kv.Key, kv.Value.OriginalName, kv.Value.Year);
                    RememberTitleKey(kv.Key, kv.Value.Name, kv.Value.Year);

                    if (kv.Value.Aka != null)
                    {
                        foreach (string title in kv.Value.Aka)
                        {
                            string normalized = Utils.StringConvert.SearchName(title);
                            if (!string.IsNullOrWhiteSpace(normalized))
                                IndexAka(normalized, kv.Value.Aka);
                        }
                    }
                }

                JacBlackLog.Information(JacBlackLogCategories.Fdb, $"словарь кодов IMDB загружен: {_titles.Count}");
            }
            catch (Exception ex)
            {
                JacBlackLog.Swallowed(JacBlackLogCategories.Fdb, "словарь кодов IMDB не загрузился", ex);
            }
        }

        /// <summary>
        /// Запомнить связку. Вызывается при записи раздачи, у которой источник
        /// сообщил код. Сама пара «код → название» не перезаписывается: первый
        /// источник обычно не хуже следующего.
        ///
        /// А вот НАЗВАНИЯ копим все. Один и тот же фильм приходит от русских
        /// трекеров как «Веном: Последний танец / Venom: The Last Dance», а от
        /// yts — как «Venom: The Last Dance» на обоих полях. Кто попал в словарь
        /// первым, тот и определял бы единственное known-название, и русского
        /// в словаре могло не оказаться вовсе — проверено 31.07.2026: записей
        /// со словом «Веном» в словаре было ноль, потому что первым пришёл yts.
        /// Из накопленных названий строится перевод запроса (см. Counterparts).
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

            RememberAka(imdb, name);
            RememberAka(imdb, originalname);
        }

        static void RememberTitleKey(string imdb, string title, int year)
        {
            string key = TitleKey(title, year);
            if (key != null)
                _byTitle.TryAdd(key, imdb);
        }

        /// <summary>
        /// Копит все написания названия под одним кодом и связывает их между
        /// собой. Пишется в тот же файл словаря отдельным полем.
        /// </summary>
        static void RememberAka(string imdb, string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return;

            string normalized = Utils.StringConvert.SearchName(title);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            if (!_titles.TryGetValue(imdb, out var entry))
                return;

            entry.Aka ??= new List<string>();

            lock (entry)
            {
                foreach (string known in entry.Aka)
                {
                    if (string.Equals(Utils.StringConvert.SearchName(known), normalized, StringComparison.Ordinal))
                    {
                        IndexAka(normalized, entry.Aka);
                        return;
                    }
                }

                entry.Aka.Add(title.Trim());
                Interlocked.Exchange(ref _dirty, 1);
                IndexAka(normalized, entry.Aka);
            }
        }

        static void IndexAka(string normalized, List<string> aka)
        {
            _akaByTitle[normalized] = aka;
        }

        /// <summary>
        /// Как ещё называется то, что просят.
        ///
        /// Зачем. Индекс базы ищет подстроку по склейке «название :
        /// оригинальное название». У русских трекеров там оба языка, поэтому
        /// они находятся хоть по-русски, хоть по-английски. А у yts, eztv и
        /// piratebay оба поля английские — в строке без единой кириллической
        /// буквы запрос «Веном» совпасть не может физически. Замер 31.07.2026:
        /// «Веном» — 196 раздач и ни одной от yts, «Venom» — 213, из них 35.
        /// Перевод запроса по словарю закрывает этот разрыв.
        /// </summary>
        public static IReadOnlyList<string> Counterparts(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<string>();

            string normalized = Utils.StringConvert.SearchName(query);
            if (string.IsNullOrWhiteSpace(normalized) || !_akaByTitle.TryGetValue(normalized, out var aka))
                return Array.Empty<string>();

            var result = new List<string>();
            lock (aka)
            {
                foreach (string title in aka)
                {
                    if (!string.Equals(Utils.StringConvert.SearchName(title), normalized, StringComparison.Ordinal))
                        result.Add(title);
                }
            }

            return result;
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

        /// <summary>
        /// Сохраняет, только если что-то добавилось с прошлого раза, и не чаще
        /// раза в пять минут.
        ///
        /// Ограничение по времени появилось вместе с выгрузкой IMDb: словарь
        /// вырос с 12 МБ до 45, а сборщик кодов с rutracker звал сохранение
        /// после КАЖДОГО добытого кода. Пока файл был маленьким, это сходило
        /// с рук; теперь это десятки мегабайт записи на одну строчку.
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
                var snapshot = new Dictionary<string, ImdbTitle>(_titles, StringComparer.OrdinalIgnoreCase);

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));

                // Обычным JSON, а не сжатым: его полезно уметь открыть глазами.
                // А вот отступы убраны — после вливания выгрузки IMDb в словаре
                // 400 тысяч записей, и на отступах файл раздувается со 45 МБ до
                // 120. Пишем через временный файл, чтобы обрыв на середине не
                // оставил половину словаря.
                string temp = Path + ".tmp";

                using (var writer = new StreamWriter(temp))
                    new JsonSerializer().Serialize(writer, snapshot);

                File.Move(temp, Path, overwrite: true);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _dirty, 1);
                JacBlackLog.Swallowed(JacBlackLogCategories.Fdb, "словарь кодов IMDB не сохранился", ex);
            }
        }
    }
}
