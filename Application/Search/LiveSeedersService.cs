using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using JacRed.Models.Api;
using Microsoft.Extensions.Caching.Memory;
using MonoTorrent;

namespace JacRed.Application.Search
{
    public interface ILiveSeeders
    {
        Task<List<Result>> ApplyAsync(List<Result> results, CancellationToken cancellationToken = default);
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

        public async Task<List<Result>> ApplyAsync(List<Result> results, CancellationToken cancellationToken = default)
        {
            var conf = AppInit.conf.scrape;
            if (conf == null || !conf.enable || results == null || results.Count == 0)
                return results;

            var sw = Stopwatch.StartNew();

            // Раздача -> её хеш и трекеры. Одинаковые хеши в выдаче схлопываются.
            var byHash = new Dictionary<string, List<Result>>(StringComparer.OrdinalIgnoreCase);
            var announcesByHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in results)
            {
                string hash = HashOf(r.MagnetUri);
                if (hash == null)
                    continue;

                if (!byHash.TryGetValue(hash, out var list))
                {
                    list = new List<Result>();
                    byHash[hash] = list;
                    announcesByHash[hash] = MagnetHygiene.AnnounceUrls(r.MagnetUri)
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
                    known[hash] = cached;
                else
                    pending.Add(hash);
            }

            if (pending.Count > 0)
                await ScrapePendingAsync(conf, pending, announcesByHash, known, sw, cancellationToken);

            foreach (var pair in known)
            {
                if (!byHash.TryGetValue(pair.Key, out var affected))
                    continue;

                foreach (var r in affected)
                {
                    r.Seeders = pair.Value.Seeders;
                    r.Peers = pair.Value.Leechers;
                }
            }

            if (conf.demoteDead)
            {
                // Ноль сидов не означает смерть наверняка — раздача может жить на DHT
                // или на трекере, которого нет в ссылке. Поэтому опускаем, а не прячем.
                return results.OrderBy(r => r.Seeders > 0 ? 0 : 1).ToList();
            }

            return results;
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

            var order = byTracker.OrderByDescending(x => x.Value.Count)
                                 .Take(Math.Max(1, conf.maxTrackers))
                                 .ToList();

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
                    // Первый ответивший трекер выигрывает: если раздачу знают двое,
                    // большее из значений всё равно неизвестно какое честнее.
                    if (known.ContainsKey(pair.Key))
                    {
                        if (pair.Value.Seeders <= known[pair.Key].Seeders)
                            continue;
                    }

                    known[pair.Key] = pair.Value;
                    _cache?.Set(CacheKey(pair.Key), pair.Value, options);
                }
            }
        }

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
