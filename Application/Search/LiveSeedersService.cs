using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Networking;
using JacBlack.Models.Api;
using JacBlack.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using MonoTorrent;

namespace JacBlack.Application.Search
{
    public interface ILiveSeeders
    {
        Task<List<Result>> ApplyAsync(List<Result> results, CancellationToken cancellationToken = default);

        /// <summary>
        /// То же самое для родного API. Раньше опрос живых сидов применялся
        /// только к выдаче индексаторов, а Лампа ходит в родной — и получала
        /// цифры из базы, которым может быть год. Замер 29.07.2026 на «Дюне»:
        /// из 280 общих раздач **67 расходились**, база показывала 89 сидов
        /// там, где живых ноль.
        /// </summary>
        Task<List<TorrentDetails>> ApplyAsync(List<TorrentDetails> torrents, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Подменяет сиды из базы на реальные, опрашивая трекеры в момент поиска.
    /// Работает по гибридной схеме: что есть в кеше — отдаётся мгновенно, остальное
    /// опрашивается в пределах жёсткого бюджета времени. Не уложились — отдаём
    /// значения из базы: опрос не имеет права сделать выдачу хуже или медленнее
    /// обещанного.
    /// </summary>
    public class LiveSeedersService : ILiveSeeders
    {
        readonly IMemoryCache _cache;

        public LiveSeedersService(IMemoryCache cache)
        {
            _cache = cache;
        }

        static string CacheKey(string hash) => "scrape:" + hash;

        public Task<List<Result>> ApplyAsync(List<Result> results, CancellationToken cancellationToken = default)
            => ApplyCoreAsync(
                results,
                r => r.MagnetUri,
                (r, counts) =>
                {
                    r.Seeders = counts.Seeders;
                    r.Peers = counts.Leechers;

                    // Отмечаем, что число получено опросом, а не взято из базы.
                    // Всё, что осталось без отметки, показывается как непроверенное.
                    if (r.info != null)
                        r.info.seedersLive = true;
                },
                r => r.Seeders,
                cancellationToken);

        public Task<List<TorrentDetails>> ApplyAsync(List<TorrentDetails> torrents, CancellationToken cancellationToken = default)
            => ApplyCoreAsync(
                torrents,
                t => t.magnet,
                (t, counts) => { t.sid = counts.Seeders; t.pir = counts.Leechers; },
                t => t.sid,
                cancellationToken);

        /// <summary>
        /// Общая часть для обоих API. Отличаются они только тем, откуда брать
        /// magnet и куда класть числа, — ради этого держать две копии опроса
        /// было бы ровно тем дублированием, из-за которого код и разъезжается.
        /// </summary>
        async Task<List<T>> ApplyCoreAsync<T>(
            List<T> results,
            Func<T, string> magnetOf,
            Action<T, TrackerScrapeClient.Counts> applyCounts,
            Func<T, int> seedersOf,
            CancellationToken cancellationToken)
        {
            var conf = AppInit.conf.scrape;
            if (conf == null || !conf.enable || results == null || results.Count == 0)
                return results;

            var sw = Stopwatch.StartNew();

            // Раздача -> её хеш и трекеры. Одинаковые хеши в выдаче схлопываются.
            var byHash = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
            var announcesByHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in results)
            {
                string hash = HashOf(magnetOf(r));
                if (hash == null)
                    continue;

                if (!byHash.TryGetValue(hash, out var list))
                {
                    list = new List<T>();
                    byHash[hash] = list;
                    announcesByHash[hash] = MagnetHygiene.AnnounceUrls(magnetOf(r))
                        .Where(a => TrackerScrapeClient.TryParseUdp(a, out _, out _))
                        .ToList();
                }

                list.Add(r);
            }

            if (byHash.Count == 0)
                return results;

            var known = new Dictionary<string, TrackerScrapeClient.Counts>(StringComparer.OrdinalIgnoreCase);
            var pending = new List<string>();

            foreach (string hash in byHash.Keys)
            {
                if (_cache != null && _cache.TryGetValue(CacheKey(hash), out TrackerScrapeClient.Counts cached))
                {
                    // Отметка «недавно спрашивали, ответа нет»: не спрашиваем
                    // снова и оставляем значение из базы.
                    if (cached.Seeders >= 0)
                        known[hash] = cached;
                }
                else
                    pending.Add(hash);
            }

            if (pending.Count > 0)
            {
                await ScrapePendingAsync(conf, pending, announcesByHash, known, sw, cancellationToken);
                FillCacheInBackground(conf, pending, known, announcesByHash);
            }

            foreach (var pair in known)
            {
                if (!byHash.TryGetValue(pair.Key, out var affected))
                    continue;

                foreach (var r in affected)
                    applyCounts(r, pair.Value);
            }

            if (conf.demoteDead)
            {
                // Ноль сидов не означает смерть наверняка — раздача может жить на DHT
                // или на трекере, которого нет в ссылке. Поэтому опускаем, а не прячем.
                return results.OrderBy(r => seedersOf(r) > 0 ? 0 : 1).ToList();
            }

            return results;
        }

        /// <summary>
        /// Выбирает трекеры так, чтобы спросить про КАЖДУЮ раздачу, а не про
        /// большинство.
        ///
        /// Раньше брались просто самые «populярные» трекеры выдачи — четыре
        /// штуки с наибольшим числом раздач. Беда в том, что раздача, чей
        /// трекер в эту четвёрку не попал, не опрашивалась вовсе, и ей
        /// доставалось старое число из базы. Отсюда жалобы в обе стороны:
        /// «показывает 5, а раздача мертва» и «показывает 5, а там 200+».
        ///
        /// Теперь набор собирается жадно: каждый следующий трекер — тот, что
        /// закрывает больше всего ещё не покрытых раздач. При том же числе
        /// запросов покрытие получается заметно полнее.
        /// </summary>
        static List<KeyValuePair<string, List<string>>> SelectCoveringTrackers(
            Dictionary<string, List<string>> byTracker, int limit)
        {
            var uncovered = new HashSet<string>(byTracker.SelectMany(x => x.Value), StringComparer.OrdinalIgnoreCase);
            var chosen = new List<KeyValuePair<string, List<string>>>();
            var left = new Dictionary<string, List<string>>(byTracker, StringComparer.OrdinalIgnoreCase);

            while (chosen.Count < limit && uncovered.Count > 0 && left.Count > 0)
            {
                string best = null;
                int bestGain = 0;

                foreach (var pair in left)
                {
                    int gain = pair.Value.Count(h => uncovered.Contains(h));
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        best = pair.Key;
                    }
                }

                // Оставшиеся трекеры не добавляют ничего нового.
                if (best == null)
                    break;

                chosen.Add(new KeyValuePair<string, List<string>>(best, left[best]));
                foreach (string h in left[best])
                    uncovered.Remove(h);

                left.Remove(best);
            }

            return chosen;
        }

        /// <summary>
        /// Дозапрашивает то, что не влезло в бюджет ответа, уже после того как
        /// ответ ушёл. Посетителю это ничего не стоит, а кеш к следующему
        /// запросу оказывается заполнен — и числа становятся честными.
        ///
        /// Токен запроса сюда не передаётся намеренно: он отменяется, как только
        /// клиент получил ответ, и фоновая работа умерла бы, не начавшись.
        /// </summary>
        void FillCacheInBackground(
            Models.AppConf.ScrapeSettings conf,
            List<string> pending,
            Dictionary<string, TrackerScrapeClient.Counts> known,
            Dictionary<string, List<string>> announcesByHash)
        {
            if (conf.backgroundFillMs <= 0 || _cache == null)
                return;

            var leftovers = pending.Where(h => !known.ContainsKey(h)).ToList();
            if (leftovers.Count == 0)
                return;

            // Своя копия настроек: бюджет здесь другой, и менять общий нельзя —
            // он же обслуживает синхронный путь.
            var slow = new Models.AppConf.ScrapeSettings
            {
                enable = conf.enable,
                budgetMs = conf.backgroundFillMs,
                trackerTimeoutMs = conf.trackerTimeoutMs,
                cacheMinutes = conf.cacheMinutes,
                noAnswerMinutes = conf.noAnswerMinutes,
                maxTrackers = conf.maxTrackers,
                maxHashesPerRequest = conf.maxHashesPerRequest,
                demoteDead = conf.demoteDead,
                backgroundFillMs = 0
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    await ScrapePendingAsync(
                        slow, leftovers, announcesByHash,
                        new Dictionary<string, TrackerScrapeClient.Counts>(StringComparer.OrdinalIgnoreCase),
                        Stopwatch.StartNew(), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    JacBlackLog.Swallowed(JacBlackLogCategories.Trackers, "догрев кеша сидов не удался", ex);
                }
            });
        }

        async Task ScrapePendingAsync(
            Models.AppConf.ScrapeSettings conf,
            List<string> pending,
            Dictionary<string, List<string>> announcesByHash,
            Dictionary<string, TrackerScrapeClient.Counts> known,
            Stopwatch sw,
            CancellationToken cancellationToken)
        {
            // Группируем по трекерам: чем больше раздач знает трекер, тем он полезнее.
            var byTracker = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string hash in pending)
            {
                if (!announcesByHash.TryGetValue(hash, out var announces))
                    continue;

                foreach (string a in announces)
                {
                    if (!byTracker.TryGetValue(a, out var list))
                    {
                        list = new List<string>();
                        byTracker[a] = list;
                    }
                    list.Add(hash);
                }
            }

            if (byTracker.Count == 0)
                return;

            var order = SelectCoveringTrackers(byTracker, Math.Max(1, conf.maxTrackers));

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(Math.Max(100, conf.budgetMs));

            var tasks = new List<Task<Dictionary<string, TrackerScrapeClient.Counts>>>();

            foreach (var (announce, hashes) in order)
            {
                var batch = hashes.Distinct(StringComparer.OrdinalIgnoreCase)
                                  .Take(Math.Max(1, conf.maxHashesPerRequest))
                                  .Select(FromHex)
                                  .Where(b => b != null)
                                  .ToList();

                if (batch.Count == 0)
                    continue;

                tasks.Add(TrackerScrapeClient.ScrapeAsync(announce, batch, conf.trackerTimeoutMs, budget.Token));
            }

            if (tasks.Count == 0)
                return;

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception)
            {
                // Часть трекеров могла не ответить — это нормально, разбираем что есть.
            }

            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, conf.cacheMinutes))
            };

            foreach (var task in tasks)
            {
                if (task.Status != TaskStatus.RanToCompletion)
                    continue;

                foreach (var pair in task.Result)
                {
                    // Выигрывает большее. Трекер знает только тех, кто анонсировался
                    // именно ему, поэтому меньшее число — это всегда неполная
                    // картина, а не более честная.
                    if (known.ContainsKey(pair.Key))
                    {
                        if (pair.Value.Seeders <= known[pair.Key].Seeders)
                            continue;
                    }

                    known[pair.Key] = pair.Value;
                    _cache?.Set(CacheKey(pair.Key), pair.Value, options);
                }
            }

            // Про раздачи, на которые никто не ответил, тоже надо помнить —
            // иначе мы спрашиваем о них снова при КАЖДОМ поиске и каждый раз
            // выжигаем весь бюджет. Замер 29.07.2026: повторный поиск того же
            // фильма занимал те же 900 мс, хотя кеш ответов был полон.
            // Помним недолго: трекер мог просто моргнуть.
            if (_cache != null && conf.noAnswerMinutes > 0)
            {
                var negative = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(conf.noAnswerMinutes)
                };

                foreach (string hash in pending)
                {
                    if (known.ContainsKey(hash))
                        continue;

                    _cache.Set(CacheKey(hash), NoAnswer, negative);
                }
            }
        }

        /// <summary>Отметка «недавно спрашивали, ответа нет». Отличается от настоящего нуля.</summary>
        static readonly TrackerScrapeClient.Counts NoAnswer = new TrackerScrapeClient.Counts(-1, -1);

        static string HashOf(string magnet)
        {
            if (string.IsNullOrWhiteSpace(magnet))
                return null;

            try
            {
                var link = MagnetLink.Parse(magnet);
                return link.InfoHashes?.V1OrV2?.ToHex()?.ToLowerInvariant();
            }
            catch (FormatException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        static byte[] FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Length != 40)
                return null;

            try
            {
                return Convert.FromHexString(hex);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
