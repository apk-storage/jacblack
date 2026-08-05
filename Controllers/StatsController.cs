using JacBlack.Infrastructure.Stats;
using JacBlack.Infrastructure.Tracks;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JacBlack.Controllers
{
    /// <summary>
    /// Stats API: /stats/torrents (stats.json), /stats/tracks (tracks-stats), /stats/meta (timestamps).
    /// </summary>
    [Route("/stats/[action]")]
    public class StatsController : Controller
    {
        /// <summary>Сводка по всем трекерам из Data/temp/stats.json (UI /stats).</summary>
        public IActionResult Torrents()
        {
            if (!AppInit.conf.openstats)
                return Content("[]", "application/json");

            return Content(StatsSummary.ReadAllJson(), "application/json");
        }

        [Route("/stats/tracks")]
        public JsonResult Tracks(bool includeTorrentDb = true)
        {
            if (!AppInit.conf.openstats)
                return Json(new { ok = false });

            var stats = TracksDB.GetExportStats(includeTorrentDb, refresh: false);
            return Json(new
            {
                ok = true,
                updatedAt = TracksDB.GetExportStatsUpdatedAt(),
                fromCache = TracksDB.LastExportStatsFromCache,
                stats
            });
        }

        /// <summary>
        /// Ход глубоких обходов: что идёт прямо сейчас, сколько осталось в
        /// очереди и где стоит возобновляемый обход.
        ///
        /// До 03.08.2026 всё это жило в семи журналах и файлах состояния на
        /// сервере, и посмотреть можно было только по ssh. Причём главного —
        /// «идёт ли обход прямо сейчас» — на диске нет вовсе: это признак
        /// занятости в памяти службы.
        /// </summary>
        [Route("/stats/crawl")]
        public JsonResult Crawl()
        {
            if (!AppInit.conf.openstats)
                return Json(new { ok = false });

            var now = DateTime.UtcNow;

            var runs = Infrastructure.Indexers.CrawlProgress.Snapshot()
                .Select(kv => new
                {
                    tracker = kv.Key,
                    startedAt = kv.Value.StartedAt,
                    finishedAt = kv.Value.FinishedAt,
                    outcome = kv.Value.Outcome,
                    running = kv.Value.FinishedAt == null,
                    minutes = (int)Math.Round(((kv.Value.FinishedAt ?? now) - kv.Value.StartedAt).TotalMinutes)
                })
                .OrderByDescending(r => r.running)
                .ThenByDescending(r => r.startedAt)
                .ToList();

            return Json(new { ok = true, updatedAt = now, runs, queues = ReadCrawlQueues() });
        }

        /// <summary>
        /// Сколько осталось в очередях и где стоят возобновляемые обходы.
        ///
        /// Очередь у трекеров устроена как «раздел → страница → список задач»,
        /// поэтому считаем вложенные списки, а не ключи верхнего уровня.
        /// Возобновляемые обходы (bitru, nnmclub, piratebay) хранят одно число
        /// в отдельном файле — как закладку в книге.
        /// </summary>
        static List<object> ReadCrawlQueues()
        {
            var result = new List<object>();
            const string dir = "Data/temp";

            if (!System.IO.Directory.Exists(dir))
                return result;

            foreach (string path in System.IO.Directory.GetFiles(dir, "*_taskParse.json").OrderBy(p => p))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(path).Replace("_taskParse", "");
                int tasks = 0;

                try
                {
                    var root = Newtonsoft.Json.Linq.JToken.Parse(System.IO.File.ReadAllText(path));
                    tasks = CountLeafTasks(root);
                }
                catch (Exception)
                {
                    // Файл могли перезаписывать прямо сейчас — покажем ноль,
                    // а не уроним всю сводку из-за одного трекера.
                    continue;
                }

                result.Add(new { tracker = name, kind = "очередь", value = tasks });
            }

            foreach (string path in System.IO.Directory.GetFiles(dir, "*.txt").OrderBy(p => p))
            {
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!file.Contains("page") && !file.Contains("cursor"))
                    continue;

                try
                {
                    string raw = System.IO.File.ReadAllText(path).Trim();
                    if (long.TryParse(raw, out long value))
                        result.Add(new { tracker = file, kind = "положение", value = (int)Math.Min(value, int.MaxValue) });
                }
                catch (Exception)
                {
                    continue;
                }
            }

            return result;
        }

        /// <summary>Считает списки на любой глубине вложенности.</summary>
        static int CountLeafTasks(Newtonsoft.Json.Linq.JToken token)
        {
            if (token is Newtonsoft.Json.Linq.JArray array)
                return array.Count;

            if (token is Newtonsoft.Json.Linq.JObject obj)
            {
                int sum = 0;
                foreach (var p in obj.Properties())
                    sum += CountLeafTasks(p.Value);
                return sum;
            }

            return 0;
        }

        /// <summary>
        /// Жив ли каждый источник — и в каком именно смысле.
        ///
        /// Заведено 03.08.2026 после жалобы «lostfilm и aniliberty не
        /// обновляются». Выяснение заняло пятиминутный обход всех 438 тысяч
        /// файлов базы, а ответ оказался тройным: у lostfilm и aniliberty молчат
        /// сами источники, зато animetosho заморожен с 8 мая — три месяца, и
        /// никто не заметил, потому что он отвечает кодом 200.
        ///
        /// Поэтому здесь четыре разных срока, а не один: «ответил», «принёс
        /// новое», «что-то изменилось» и «самая свежая дата у самого источника».
        /// Последний и отличает «источник умер» от «мы к нему не ходим».
        /// </summary>
        [Route("/stats/trackers")]
        public JsonResult Trackers()
        {
            if (!AppInit.conf.openstats)
                return Json(new { ok = false });

            var now = DateTime.UtcNow;

            var rows = new List<(string Tracker, Infrastructure.Indexers.TrackerFreshnessEntry E, int? Silent)>();

            foreach (var kv in Infrastructure.Indexers.TrackerFreshness.Snapshot())
            {
                // Сколько суток источник не выкладывает ничего нового.
                // Именно это число и надо показывать человеку.
                int? silent = kv.Value.NewestRelease == null
                    ? null
                    : (int)Math.Floor((now - kv.Value.NewestRelease.Value).TotalDays);

                rows.Add((kv.Key, kv.Value, silent));
            }

            // Самые молчаливые сверху — это и есть список того, что чинить.
            rows.Sort((a, b) => (b.Silent ?? -1).CompareTo(a.Silent ?? -1));

            var trackers = rows.Select(r => new
            {
                tracker = r.Tracker,
                lastSeen = r.E.LastSeen,
                lastAdded = r.E.LastAdded,
                lastChanged = r.E.LastChanged,
                newestRelease = r.E.NewestRelease,
                silentDays = r.Silent
            });

            return Json(new { ok = true, updatedAt = now, trackers });
        }

        /// <summary>
        /// Чему в выдаче можно верить: откуда берутся живые числа раздающих,
        /// сколько раздач опознано удалёнными, насколько полон словарь кодов.
        ///
        /// Заведено потому, что страница статистики показывала только объёмы
        /// базы — сколько чего накоплено. А главный вопрос человека другой:
        /// свежие ли числа он видит. Ответ на него до 02.08.2026 нигде не
        /// показывался, хотя всё это уже считалось.
        /// </summary>
        [Route("/stats/quality")]
        public JsonResult Quality()
        {
            if (!AppInit.conf.openstats)
                return Json(new { ok = false });

            return Json(new
            {
                ok = true,

                // Раздачи, которых на трекере уже нет: скачать нельзя, из выдачи убраны.
                deadReleases = Infrastructure.Persistence.DeadReleases.Count,

                // Словарь кодов IMDB — им разводятся тёзки.
                imdbCodes = Infrastructure.Persistence.ImdbIndex.Count,

                // Словарь кодов Кинопоиска — то же для русского кино, у
                // которого кода IMDB нет ни на одном трекере. Число здесь
                // и есть мера того, скольких тёзок мы вообще способны
                // развести: пока код не добыт, работает прежний разбор по
                // названию, году и типу.
                kinopoiskCodes = Infrastructure.Persistence.KinopoiskIndex.Count,

                // Сколько часов запись считается проверенной после обхода.
                freshHours = Infrastructure.Indexers.SeedersFreshness.FreshHours,

                // У кого числа берутся живым опросом, а не из базы.
                liveSeeders = new
                {
                    // Спрашиваются прямо в запросе — их поиск отвечает быстро.
                    inline = new[] { "nnmclub", "kinozal" },

                    // Спрашиваются после ответа, числа сохраняются на четверть часа:
                    // вход у них занимает секунды и задерживал бы выдачу.
                    background = new[] { "toloka", "bitru", "rutracker" },

                    // Опрашиваются по протоколу scrape — это открытые трекеры.
                    scrape = new[] { "rutor", "megapeer", "torrentby", "eztv", "yts", "piratebay", "nyaa", "knaben" },

                    // Счётчиков не публикует вовсе: единица в записи проставлена разбором.
                    silent = new[] { "lostfilm" }
                }
            });
        }

        [Route("/stats/meta")]
        public JsonResult Meta()
        {
            if (!AppInit.conf.openstats)
                return Json(new { ok = false });

            DateTime? updatedAt = StatsCollector.LastCollectedAtUtc ?? StatsCollector.TryReadStatsMetaUpdatedAt();

            return Json(new
            {
                ok = true,
                updatedAt,
                updatedAtLocal = updatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                tracksStatsUpdatedAt = TracksDB.GetExportStatsUpdatedAt()
            });
        }
    }
}
