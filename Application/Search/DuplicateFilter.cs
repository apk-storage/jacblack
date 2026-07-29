using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using JacRed.Models.Details;

namespace JacRed.Application.Search
{
    /// <summary>
    /// Убирает из выдачи повторы одной и той же раздачи ВНУТРИ одного трекера.
    ///
    /// Откуда они берутся: адрес раздачи — это ключ записи, а домены трекеров
    /// меняются. Одна и та же тема rutracker лежит дважды — под `rutracker.net`
    /// и под `rutracker.org`, номер темы у обеих один. Замер 29.07.2026 на
    /// выдаче по «Аватару»: из 492 записей 36 были такими повторами.
    ///
    /// Раздачу, встреченную на РАЗНЫХ трекерах, не трогаем: у них разные сиды
    /// и разные страницы, человеку полезно видеть оба варианта.
    /// </summary>
    public static class DuplicateFilter
    {
        static readonly Regex RxHash = new Regex("btih:([0-9a-fA-F]{40})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Оставляет из каждой группы «трекер + хеш» одну запись — с бо́льшим
        /// числом сидов, а при равенстве более свежую.
        /// </summary>
        public static List<T> RemoveSameTrackerDuplicates<T>(IEnumerable<T> items, Func<T, TorrentDetails> selector)
        {
            var best = new Dictionary<string, (T item, TorrentDetails details)>(StringComparer.OrdinalIgnoreCase);
            var result = new List<T>();

            foreach (var raw in items)
            {
                var t = selector(raw);
                string key = KeyOf(t);

                if (key == null)
                {
                    // Без хеша сравнивать нечего — пропускаем как есть.
                    result.Add(raw);
                    continue;
                }

                if (!best.TryGetValue(key, out var had))
                {
                    best[key] = (raw, t);
                    continue;
                }

                if (IsBetter(t, had.details))
                    best[key] = (raw, t);
            }

            foreach (var pair in best.Values)
                result.Add(pair.item);

            return result;
        }

        static string KeyOf(TorrentDetails t)
        {
            if (t == null || string.IsNullOrEmpty(t.magnet))
                return null;

            var m = RxHash.Match(t.magnet);
            if (!m.Success)
                return null;

            return (t.trackerName ?? "?") + ":" + m.Groups[1].Value.ToLowerInvariant();
        }

        static bool IsBetter(TorrentDetails candidate, TorrentDetails current)
        {
            if (candidate.sid != current.sid)
                return candidate.sid > current.sid;

            return candidate.updateTime > current.updateTime;
        }
    }
}
