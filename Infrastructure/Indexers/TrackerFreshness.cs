using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using JacRed.Infrastructure.Logging;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Indexers
{
    /// <summary>Что известно про свежесть одного источника.</summary>
    public class TrackerFreshnessEntry
    {
        /// <summary>Когда через базу в последний раз прошла хоть одна запись этого источника.</summary>
        [JsonProperty("seen")]
        public DateTime? LastSeen { get; set; }

        /// <summary>Когда в последний раз появилась НОВАЯ раздача.</summary>
        [JsonProperty("added")]
        public DateTime? LastAdded { get; set; }

        /// <summary>Когда в последний раз запись действительно изменилась.</summary>
        [JsonProperty("changed")]
        public DateTime? LastChanged { get; set; }

        /// <summary>
        /// Самая свежая дата публикации, которую сообщил сам источник.
        /// Это и есть ответ на «а он вообще ещё что-нибудь выкладывает».
        /// </summary>
        [JsonProperty("newest")]
        public DateTime? NewestRelease { get; set; }
    }

    /// <summary>
    /// Свежесть по источникам: пишется в момент записи, а не считается обходом.
    ///
    /// Зачем. 03.08.2026 пришлось выяснять, почему «lostfilm и aniliberty не
    /// обновляются», и ответ занял пятиминутный обход всех 438 тысяч файлов
    /// базы. Оказалось: у lostfilm и aniliberty источники сами ничего не
    /// публикуют с 31 июля, а вот animetosho заморожен с 8 мая — три месяца, и
    /// никто не заметил, потому что он исправно отвечает кодом 200 и наш обход
    /// исправно перезаписывает майские данные.
    ///
    /// Отсюда правило: «трекер отвечает» и «трекер приносит новое» — разные
    /// утверждения, и второе надо мерить отдельно. Различаем четыре момента:
    /// когда источник в последний раз ответил хоть чем-то, когда принёс новую
    /// раздачу, когда что-то реально изменилось и какой самой свежей датой
    /// публикации он вообще располагает.
    ///
    /// Стоимость околонулевая: словарь на два десятка записей, обновляется
    /// присваиванием, сохраняется вместе с базой.
    /// </summary>
    public static class TrackerFreshness
    {
        const string Path = "Data/tracker-freshness.json";

        static readonly ConcurrentDictionary<string, TrackerFreshnessEntry> _entries =
            new ConcurrentDictionary<string, TrackerFreshnessEntry>(StringComparer.OrdinalIgnoreCase);

        static int _dirty;
        static int _loaded;

        public static void Load()
        {
            if (Interlocked.Exchange(ref _loaded, 1) == 1)
                return;

            try
            {
                if (!File.Exists(Path))
                    return;

                var data = JsonConvert.DeserializeObject<Dictionary<string, TrackerFreshnessEntry>>(File.ReadAllText(Path));
                if (data == null)
                    return;

                foreach (var kv in data)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                        _entries[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Fdb, "отметки свежести источников не загрузились", ex);
            }
        }

        static TrackerFreshnessEntry Entry(string tracker)
            => _entries.GetOrAdd(tracker, _ => new TrackerFreshnessEntry());

        /// <summary>
        /// Источник ответил, и запись дошла до базы. Заодно запоминаем самую
        /// свежую дату публикации: именно она отличает «источник молчит» от
        /// «мы к нему не ходим».
        /// </summary>
        public static void NoteSeen(string tracker, DateTime releaseCreated)
        {
            if (string.IsNullOrWhiteSpace(tracker))
                return;

            var e = Entry(tracker);
            e.LastSeen = DateTime.UtcNow;

            // Даты из будущего игнорируем: у некоторых источников время сервера
            // уходит вперёд, и одна такая запись навсегда испортила бы отметку.
            if (releaseCreated > new DateTime(2000, 1, 1)
                && releaseCreated < DateTime.UtcNow.AddDays(2)
                && (e.NewestRelease == null || releaseCreated > e.NewestRelease))
            {
                e.NewestRelease = releaseCreated;
            }

            Interlocked.Exchange(ref _dirty, 1);
        }

        public static void NoteAdded(string tracker)
        {
            if (string.IsNullOrWhiteSpace(tracker))
                return;

            var e = Entry(tracker);
            e.LastAdded = DateTime.UtcNow;
            e.LastChanged = e.LastAdded;
            Interlocked.Exchange(ref _dirty, 1);
        }

        public static void NoteChanged(string tracker)
        {
            if (string.IsNullOrWhiteSpace(tracker))
                return;

            Entry(tracker).LastChanged = DateTime.UtcNow;
            Interlocked.Exchange(ref _dirty, 1);
        }

        public static IReadOnlyDictionary<string, TrackerFreshnessEntry> Snapshot()
            => new Dictionary<string, TrackerFreshnessEntry>(_entries, StringComparer.OrdinalIgnoreCase);

        public static void SaveIfDirty()
        {
            if (Interlocked.Exchange(ref _dirty, 0) == 0)
                return;

            try
            {
                var snapshot = new Dictionary<string, TrackerFreshnessEntry>(_entries, StringComparer.OrdinalIgnoreCase);

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));

                string temp = Path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
                File.Move(temp, Path, overwrite: true);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _dirty, 1);
                JacRedLog.Swallowed(JacRedLogCategories.Fdb, "отметки свежести источников не сохранились", ex);
            }
        }
    }
}
