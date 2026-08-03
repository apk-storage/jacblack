using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacBlack.Controllers.Cron
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
                    JacBlackLog.Warning(JacBlackLogCategories.Host,
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
                живые = alive,
                анонсы = await CheckDefaultTrackersAsync(cancellationToken)
            });
        }

        /// <summary>
        /// Проверяет трекеры, которые мы САМИ дописываем в ссылки без анонсов.
        ///
        /// Их пять, и они уходят в 262 тысячи раздач kinozal и nnmclub. Если
        /// один умрёт, мы будем дописывать мёртвый адрес всем подряд и никогда
        /// об этом не узнаем — как это уже вышло с доменом kinozal.tv.
        /// </summary>
        async Task<object> CheckDefaultTrackersAsync(CancellationToken cancellationToken)
        {
            var conf = AppInit.conf?.magnet;
            var list = conf?.defaultTrackers;

            if (conf == null || !conf.addDefaultTrackers || list == null || list.Count == 0)
                return new { note = "дописывание анонсов выключено" };

            var alive = new List<string>();
            var silent = new List<string>();

            // Пустой запрос: нам нужен сам факт ответа, а не числа.
            var probe = new List<byte[]> { new byte[20] };

            foreach (string announce in list)
            {
                if (string.IsNullOrWhiteSpace(announce))
                    continue;

                try
                {
                    var answer = await Infrastructure.Networking.TrackerScrapeClient
                        .ScrapeAsync(announce, probe, 4000, cancellationToken);

                    // Ответ есть — трекер жив, даже если про эту раздачу не знает.
                    if (answer != null)
                        alive.Add(announce);
                    else
                        silent.Add(announce);
                }
                catch (Exception)
                {
                    silent.Add(announce);
                }
            }

            foreach (string announce in silent)
            {
                JacBlackLog.Warning(JacBlackLogCategories.Host,
                    $"анонс {announce} не отвечает, а мы дописываем его в ссылки без трекеров. " +
                    "Замените в magnet.defaultTrackers, иначе клиент будет стучаться в пустоту");
            }

            return new { живых = alive.Count, молчат = silent };
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
