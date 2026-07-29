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
        public static List<TorrentDetails> RemoveSameTrackerDuplicates(IEnumerable<TorrentDetails> items, Func<TorrentDetails, TorrentDetails> selector)
            => Remove(items, t => t.trackerName, t => t.magnet, t => t.sid, t => t.updateTime);

        /// <summary>
        /// То же для выдачи индексаторов. Раньше повторы схлопывались только
        /// в родном API — то самое, из-за чего правки разъезжаются по путям
        /// выдачи: их три, а делаешь в одном.
        /// </summary>
        public static List<Models.Api.Result> RemoveSameTrackerDuplicates(IEnumerable<Models.Api.Result> items)
            => Remove(items, r => r.Tracker, r => r.MagnetUri, r => r.Seeders, r => r.PublishDate);

        static List<T> Remove<T>(
            IEnumerable<T> items,
            Func<T, string> trackerOf,
            Func<T, string> magnetOf,
            Func<T, int> seedersOf,
            Func<T, DateTime> updatedOf)
        {
            if (items == null)
                return new List<T>();

            var best = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            var result = new List<T>();

            foreach (var raw in items)
            {
                string key = KeyOf(trackerOf(raw), magnetOf(raw));

                if (key == null)
                {
                    // Без хеша сравнивать нечего — пропускаем как есть.
                    result.Add(raw);
                    continue;
                }

                if (!best.TryGetValue(key, out var had))
                {
                    best[key] = raw;
                    continue;
                }

                if (seedersOf(raw) != seedersOf(had)
                    ? seedersOf(raw) > seedersOf(had)
                    : updatedOf(raw) > updatedOf(had))
                {
                    best[key] = raw;
                }
            }

            result.AddRange(best.Values);
            return result;
        }

        static string KeyOf(string tracker, string magnet)
        {
            if (string.IsNullOrEmpty(magnet))
                return null;

            var m = RxHash.Match(magnet);
            if (!m.Success)
                return null;

            return (tracker ?? "?") + ":" + m.Groups[1].Value.ToLowerInvariant();
        }
    }
}
