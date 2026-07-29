using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    /// <summary>
    /// Сторож доменов, на которые ведут ссылки в базе.
    ///
    /// Домены трекеров меняются, а в записях остаётся тот, что был на момент
    /// обхода. Так у kinozal накопилось **77% ссылок на `kinozal.tv`**, который
    /// перестал резолвиться, — и узнали мы об этом случайно, спустя месяцы.
    /// Проверка дешёвая (резолв имени, не запрос страницы), поэтому гоняется
    /// раз в неделю и просто пишет в лог, что перестало отвечать.
    /// </summary>
    [Route("/cron/hosts/[action]")]
    public class HostsHealthController : BaseController
    {
        public HostsHealthController(IMemoryCache memoryCache) : base(memoryCache)
        {
        }

        /// <summary>
        /// Собирает домены из выборки записей и проверяет, что они резолвятся.
        /// sample — сколько шардов посмотреть; по умолчанию хватает, чтобы
        /// увидеть все живые домены (их всего два десятка).
        /// </summary>
        async public Task<IActionResult> Check(int sample = 4000, CancellationToken cancellationToken = default)
        {
            var hosts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var trackersByHost = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            var keys = FileDB.masterDb.Keys.ToArray();
            int step = Math.Max(1, keys.Length / Math.Max(1, sample));

            for (int i = 0; i < keys.Length; i += step)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var db = FileDB.OpenRead(keys[i], cache: false);
                if (db == null)
                    continue;

                foreach (var t in db.Values)
                {
                    string host = HostOf(t?.url);
                    if (host == null)
                        continue;

                    hosts[host] = hosts.GetValueOrDefault(host) + 1;

                    if (!trackersByHost.TryGetValue(host, out var set))
                        trackersByHost[host] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (!string.IsNullOrEmpty(t.trackerName))
                        set.Add(t.trackerName);
                }
            }

            var dead = new List<object>();
            var alive = new List<object>();

            foreach (var pair in hosts.OrderByDescending(x => x.Value))
            {
                bool resolves = await ResolvesAsync(pair.Key, cancellationToken);

                var row = new
                {
                    домен = pair.Key,
                    ссылок = pair.Value,
                    трекеры = trackersByHost[pair.Key].OrderBy(x => x).ToArray()
                };

                if (resolves)
                    alive.Add(row);
                else
                {
                    dead.Add(row);
                    JacRedLog.Warning(JacRedLogCategories.Host,
                        $"домен {pair.Key} перестал резолвиться, а на него ведут ссылки трекеров: {string.Join(", ", trackersByHost[pair.Key])}. " +
                        "Если у трекера появилось живое зеркало — добавьте подмену в urlhygiene.replaceHosts");
                }
            }

            return Json(new
            {
                ok = true,
                посмотреноШардов = Math.Min(sample, keys.Length),
                доменов = hosts.Count,
                мёртвые = dead,
                живые = alive
            });
        }

        static string HostOf(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return null;

            try { return new Uri(url).Host; }
            catch (UriFormatException) { return null; }
        }

        static async Task<bool> ResolvesAsync(string host, CancellationToken cancellationToken)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                return addresses != null && addresses.Length > 0;
            }
            catch (Exception)
            {
                // Именно этот случай нас и интересует: имени больше нет.
                return false;
            }
        }
    }
}
