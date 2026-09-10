using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacBlack.Models.Details;

namespace JacBlack.Application.Search
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

        /// <summary>Склеенная раздача: одна запись и адреса всех её копий.</summary>
        public sealed class Merged
        {
            public TorrentDetails Item { get; init; }

            /// <summary>Адреса всех копий, начиная с основной.</summary>
            public List<string> Sources { get; init; }
        }

        /// <summary>
        /// Схлопывает копии ОДНОГО файла, найденные на РАЗНЫХ трекерах.
        ///
        /// Зачем. Инфохеш — это отпечаток самого файла: совпал значит файл тот
        /// же, и качаться он будет один. Замер 15.08.2026 по «Дому дракона»:
        /// на сайте 289 строк, а уникальных файлов среди них 149 — то есть
        /// 140 строк повторы, и один файл показывался восемь раз.
        ///
        /// Раздающих берём МАКСИМУМ по копиям, а не сумму. Сумма была бы
        /// враньём: один человек раздаёт один файл и виден всем трекерам
        /// сразу, поэтому сложение посчитало бы его столько раз, на скольких
        /// трекерах лежит копия. Максимум же ничего не выдумывает — это
        /// показание того трекера, который видит раздачу лучше остальных.
        /// На живом примере это и есть разница между «54» и «2»: кинозал мы
        /// опрашиваем в момент поиска, а у остальных копий в базе снимок,
        /// сделанный неизвестно когда.
        ///
        /// Название выживает одно, поэтому метку Dolby Vision переносим со
        /// всех копий: файл один, а описывают трекеры по-разному, и раздача
        /// с DV легко выглядела обычной HDR.
        /// </summary>
        public static List<Merged> MergeAcrossTrackers(IEnumerable<TorrentDetails> items)
        {
            var merged = new Dictionary<string, Merged>(StringComparer.OrdinalIgnoreCase);
            var result = new List<Merged>();

            foreach (var t in items ?? Array.Empty<TorrentDetails>())
            {
                string hash = HashOf(t?.magnet);

                if (t == null || hash == null)
                {
                    // Без хеша сравнивать нечего — оставляем как есть.
                    if (t != null)
                        result.Add(new Merged { Item = t, Sources = new List<string> { t.url } });
                    continue;
                }

                if (!merged.TryGetValue(hash, out var had))
                {
                    // Основную запись КЛОНИРУЕМ: сюда приходят объекты самой
                    // базы, и правки ниже (имя трекера, сиды, название) писались
                    // прямо в неё. Составное имя оставалось в записи навсегда,
                    // следующий поиск складывал его снова — так и накапливалось
                    // «bitru, kinozal, kinozal, rutor, bitru». Путь чтения не
                    // должен менять то, что читает.
                    merged[hash] = new Merged
                    {
                        Item = (TorrentDetails)t.Clone(),
                        Sources = new List<string> { t.url }
                    };
                    continue;
                }

                var main = had.Item;

                // Складываем ИМЕНА, а не строки: у копии поле само может быть
                // списком, и сравнение подстрокой его не узнавало.
                main.trackerName = Infrastructure.Parsing.TrackerNames.Merge(main.trackerName, t.trackerName);

                if (t.sid > main.sid)
                    main.sid = t.sid;

                if (t.pir > main.pir)
                    main.pir = t.pir;

                main.title = Infrastructure.Parsing.DolbyVisionTag.Preserve(main.title, t.title);

                if (!string.IsNullOrEmpty(t.url) && !had.Sources.Contains(t.url, StringComparer.OrdinalIgnoreCase))
                    had.Sources.Add(t.url);
            }

            result.AddRange(merged.Values);
            return result;
        }

        static string HashOf(string magnet)
        {
            if (string.IsNullOrEmpty(magnet))
                return null;

            var m = RxHash.Match(magnet);
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }

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
