using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;
using Newtonsoft.Json;

namespace JacRed.Application.Maintenance
{
    /// <summary>
    /// Сторож молчаливых отказов. Трекер меняет вёрстку, парсер начинает возвращать
    /// пусто, и без такой проверки это выясняется через месяц по отсутствию новинок.
    ///
    /// Считает по самой базе, а не по логам: у каждого парсера свой формат итоговой
    /// строки, и сравнивать их бессмысленно, а `updateTime` записи — факт.
    /// </summary>
    public class ParserHealthService
    {
        const string LogName = "health";
        const string ReportPath = "Data/temp/parser_health.json";

        static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public class TrackerHealth
        {
            public string tracker { get; set; }
            public int total { get; set; }
            public int last24h { get; set; }
            public int last7d { get; set; }
            public string lastWrite { get; set; }
            public int silentDays { get; set; }
            public string verdict { get; set; }
        }

        public class HealthReport
        {
            public bool ok { get; set; } = true;
            public string builtAt { get; set; }
            public double seconds { get; set; }
            public int totalRecords { get; set; }
            public List<TrackerHealth> trackers { get; set; } = new List<TrackerHealth>();
            public List<string> alarms { get; set; } = new List<string>();
        }

        /// <summary>Последний посчитанный отчёт. Пересчёт запускается кроном.</summary>
        public HealthReport LastReport()
        {
            try
            {
                if (!File.Exists(ReportPath))
                    return new HealthReport { ok = false, builtAt = null, alarms = { "отчёт ещё не строился" } };

                return JsonConvert.DeserializeObject<HealthReport>(File.ReadAllText(ReportPath));
            }
            catch (IOException)
            {
                return new HealthReport { ok = false, alarms = { "отчёт не читается" } };
            }
            catch (JsonException)
            {
                return new HealthReport { ok = false, alarms = { "отчёт повреждён" } };
            }
        }

        /// <summary>
        /// Полный обход базы. Дорого, поэтому по расписанию раз в несколько часов,
        /// а не на каждый запрос.
        /// </summary>
        public HealthReport Rebuild(CancellationToken cancellationToken = default)
        {
            if (!_lock.Wait(0, cancellationToken))
                return LastReport();

            var sw = Stopwatch.StartNew();

            try
            {
                var now = DateTime.UtcNow;
                var day = now.AddDays(-1);
                var week = now.AddDays(-7);

                var total = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var d1 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var d7 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var last = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                int records = 0;

                foreach (var item in FileDB.masterDb.ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var db = FileDB.OpenRead(item.Key, cache: false);
                    if (db == null)
                        continue;

                    foreach (var kv in db)
                    {
                        var t = kv.Value;
                        if (t == null)
                            continue;

                        string name = string.IsNullOrWhiteSpace(t.trackerName) ? "?" : t.trackerName;
                        records++;
                        total[name] = total.GetValueOrDefault(name) + 1;

                        var u = t.updateTime;
                        if (u > day) d1[name] = d1.GetValueOrDefault(name) + 1;
                        if (u > week) d7[name] = d7.GetValueOrDefault(name) + 1;
                        if (!last.TryGetValue(name, out var had) || u > had)
                            last[name] = u;
                    }
                }

                var report = new HealthReport
                {
                    builtAt = now.ToString("yyyy-MM-dd HH:mm:ss") + " UTC",
                    totalRecords = records,
                    seconds = Math.Round(sw.Elapsed.TotalSeconds, 1)
                };

                foreach (var name in total.Keys.OrderByDescending(k => total[k]))
                {
                    // Составные имена вида "rutracker, kinozal" появляются при слиянии
                    // дубликатов и не относятся ни к одному парсеру — их не судим.
                    bool merged = name.Contains(',');

                    var lw = last.GetValueOrDefault(name);
                    int silent = lw == default ? 9999 : (int)(now - lw).TotalDays;

                    string verdict;
                    if (merged) verdict = "слияние дубликатов, не парсер";
                    else if (d1.GetValueOrDefault(name) > 0) verdict = "пишет";
                    else if (d7.GetValueOrDefault(name) > 0) verdict = "пишет редко";
                    else if (silent > 30) verdict = "МОЛЧИТ давно";
                    else verdict = "МОЛЧИТ";

                    var th = new TrackerHealth
                    {
                        tracker = name,
                        total = total[name],
                        last24h = d1.GetValueOrDefault(name),
                        last7d = d7.GetValueOrDefault(name),
                        lastWrite = lw == default ? null : lw.ToString("yyyy-MM-dd HH:mm"),
                        silentDays = silent == 9999 ? -1 : silent,
                        verdict = verdict
                    };
                    report.trackers.Add(th);

                    if (!merged && verdict.StartsWith("МОЛЧИТ"))
                        report.alarms.Add($"{name}: ни одной записи за неделю, последняя {th.lastWrite ?? "никогда"}");
                }

                Save(report);

                ParserLog.Write(LogName,
                    $"Отчёт построен за {report.seconds}с | записей {records}, трекеров {report.trackers.Count}, тревог {report.alarms.Count}");

                return report;
            }
            catch (OperationCanceledException)
            {
                return new HealthReport { ok = false, alarms = { "прервано" } };
            }
            catch (Exception ex)
            {
                ParserLog.Write(LogName, $"Ошибка: {ex.Message}");
                return new HealthReport { ok = false, alarms = { ex.Message } };
            }
            finally
            {
                _lock.Release();
            }
        }

        static void Save(HealthReport report)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
