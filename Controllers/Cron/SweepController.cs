using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Application.Maintenance;
using JacRed.Infrastructure.Persistence;
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

        /// <summary>
        /// Сколько раздач сколько раз подряд показали ноль. Полный обход базы,
        /// поэтому дёргать вручную, а не по расписанию.
        /// </summary>
        public JsonResult Stats()
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

            return Json(new
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
            });
        }
    }
}
