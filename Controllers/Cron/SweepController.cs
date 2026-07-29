using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Application.Maintenance;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/sweep/[action]")]
    public class SweepController : BaseController
    {
        readonly AliveSweepService _sweep;

        public SweepController(IMemoryCache memoryCache, AliveSweepService sweep) : base(memoryCache)
        {
            _sweep = sweep;
        }

        /// <summary>Очередная порция проверки живости. Место обхода запоминается между прогонами.</summary>
        public async Task<JsonResult> Run(CancellationToken cancellationToken)
        {
            return Json(await _sweep.RunAsync(cancellationToken));
        }

        const string StatsCachePath = "Data/temp/sweep-stats.json";

        /// <summary>
        /// Сколько раздач сколько раз подряд показали ноль.
        ///
        /// Это полный обход базы: на 1.27 миллиона раздач он занимает две с
        /// половиной минуты и держит поток запроса всё это время. Поэтому по
        /// умолчанию отдаётся последний посчитанный отчёт, а пересчёт идёт
        /// только по явной просьбе — `?fresh=true`.
        /// </summary>
        public IActionResult Stats(bool fresh = false)
        {
            if (!fresh)
            {
                var cached = ReadCachedStats();
                if (cached != null)
                {
                    // Отдаём готовым текстом, а не через Json(): у приложения
                    // системный сериализатор, и JObject от Newtonsoft он
                    // превращает в пустые массивы — на этом я и попался.
                    return Content(cached.ToString(Newtonsoft.Json.Formatting.None), "application/json; charset=utf-8");
                }
            }

            var report = BuildStats();

            try
            {
                System.IO.File.WriteAllText(StatsCachePath,
                    Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Fdb, "отчёт проверки живости не сохранился", ex);
            }

            return Json(report);
        }

        static Newtonsoft.Json.Linq.JObject ReadCachedStats()
        {
            try
            {
                if (!System.IO.File.Exists(StatsCachePath))
                    return null;

                var jo = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(StatsCachePath));
                jo["посчитано"] = System.IO.File.GetLastWriteTimeUtc(StatsCachePath);
                jo["подсказка"] = "это последний посчитанный отчёт; пересчёт — ?fresh=true, он обходит всю базу";
                return jo;
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Fdb, "отчёт проверки живости не прочитался", ex, LogLevel.Debug);
                return null;
            }
        }

        object BuildStats()
        {
            var byCount = new SortedDictionary<int, int>();
            var deadByTracker = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var totalByTracker = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int total = 0, checkedEver = 0;
            int threshold = Math.Max(1, AppInit.conf.sweep?.deadThreshold ?? 5);

            foreach (var item in FileDB.masterDb.ToArray())
            {
                var db = FileDB.OpenRead(item.Key, cache: false);
                if (db == null)
                    continue;

                foreach (var kv in db)
                {
                    var t = kv.Value;
                    if (t == null)
                        continue;

                    total++;
                    string tr = t.trackerName ?? "?";
                    totalByTracker[tr] = totalByTracker.GetValueOrDefault(tr) + 1;

                    if (t.lastAliveCheck.HasValue)
                        checkedEver++;

                    int d = t.deadChecks;
                    byCount[d] = byCount.GetValueOrDefault(d) + 1;

                    if (d >= threshold)
                        deadByTracker[tr] = deadByTracker.GetValueOrDefault(tr) + 1;
                }
            }

            return new
            {
                ok = true,
                порог = threshold,
                удалениеВключено = AppInit.conf.sweep?.deleteDead ?? false,
                всегоРаздач = total,
                хотьРазПроверено = checkedEver,
                поСчётчику = byCount.ToDictionary(x => x.Key.ToString(), x => x.Value),
                достиглиПорога = byCount.Where(x => x.Key >= threshold).Sum(x => x.Value),
                поТрекерам = totalByTracker
                    .OrderByDescending(x => x.Value)
                    .Take(20)
                    .ToDictionary(x => x.Key, x => new
                    {
                        всего = x.Value,
                        мёртвых = deadByTracker.GetValueOrDefault(x.Key)
                    })
            };
        }
    }
}
