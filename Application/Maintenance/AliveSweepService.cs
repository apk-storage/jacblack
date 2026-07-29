using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Application.Search;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using MonoTorrent;

namespace JacRed.Application.Maintenance
{
    /// <summary>
    /// Идёт по базе и выясняет, живы ли раздачи. Ведёт счётчик подряд идущих нулей:
    /// признак жизни сбрасывает его, молчание трекера не трогает вовсе — «не знаю»
    /// это не «мертва». База миллионная, поэтому обход порционный, с запоминанием
    /// места между прогонами.
    /// </summary>
    public class AliveSweepService
    {
        const string LogName = "sweep";
        const string CursorPath = "Data/temp/sweep_cursor.txt";

        static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public class SweepReport
        {
            public bool ok { get; set; } = true;
            public string note { get; set; }
            public int keysProcessed { get; set; }
            public int torrents { get; set; }
            public int asked { get; set; }
            public int alive { get; set; }
            public int zero { get; set; }
            public int unknown { get; set; }
            public int revived { get; set; }
            public int reachedThreshold { get; set; }
            public int deleted { get; set; }
            public double seconds { get; set; }
            public string cursor { get; set; }
            public bool wrapped { get; set; }
        }

        public async Task<SweepReport> RunAsync(CancellationToken cancellationToken = default)
        {
            var conf = AppInit.conf.sweep;
            var report = new SweepReport();

            if (conf == null || !conf.enable)
            {
                report.ok = false;
                report.note = "отключено в конфиге";
                return report;
            }

            if (!await _lock.WaitAsync(0, cancellationToken))
            {
                report.ok = false;
                report.note = "предыдущий прогон ещё идёт";
                return report;
            }

            var sw = Stopwatch.StartNew();

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, conf.maxSeconds)));

                var keys = FileDB.masterDb.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                if (keys.Count == 0)
                {
                    report.note = "база пуста";
                    return report;
                }

                string cursor = ReadCursor();
                int start = 0;
                if (!string.IsNullOrEmpty(cursor))
                {
                    // Ищем место, где остановились. Ключи могли измениться — берём ближайший следующий.
                    int idx = keys.BinarySearch(cursor, StringComparer.Ordinal);
                    start = idx >= 0 ? idx + 1 : ~idx;
                }

                if (start >= keys.Count)
                {
                    start = 0;
                    report.wrapped = true;
                }

                var slice = keys.Skip(start).Take(Math.Max(100, conf.batchKeys)).ToList();
                ParserLog.Write(LogName, $"Прогон начат: ключей {slice.Count}, с позиции {start} из {keys.Count}");

                // ── Сбор ──
                var byKey = new Dictionary<string, List<(string url, string hash)>>();
                var announcesByHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (string key in slice)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    var db = FileDB.OpenRead(key, cache: false);
                    if (db == null)
                        continue;

                    foreach (var kv in db)
                    {
                        var t = kv.Value;
                        if (t == null || string.IsNullOrWhiteSpace(t.magnet))
                            continue;

                        string hash = HashOf(t.magnet);
                        if (hash == null)
                            continue;

                        var announces = MagnetHygiene.AnnounceUrls(t.magnet)
                            .Where(a => TrackerScrapeClient.TryParseUdp(a, out _, out _))
                            .ToList();
                        if (announces.Count == 0)
                            continue;

                        if (!byKey.TryGetValue(key, out var list))
                        {
                            list = new List<(string, string)>();
                            byKey[key] = list;
                        }
                        list.Add((kv.Key, hash));

                        if (!announcesByHash.ContainsKey(hash))
                            announcesByHash[hash] = announces;

                        report.torrents++;
                    }
                }

                report.keysProcessed = slice.Count;

                if (announcesByHash.Count == 0)
                {
                    report.note = "в этой порции некого спрашивать";
                    SaveCursor(slice.LastOrDefault());
                    report.cursor = slice.LastOrDefault();
                    report.seconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
                    return report;
                }

                // ── Опрос ──
                var counts = await ScrapeAllAsync(conf, announcesByHash, cts.Token);
                report.asked = announcesByHash.Count;

                // ── Запись ──
                ApplyResults(conf, byKey, counts, report);

                FileDB.SaveChangesToFile();

                string last = slice.LastOrDefault();
                SaveCursor(last);
                report.cursor = last;
                report.seconds = Math.Round(sw.Elapsed.TotalSeconds, 1);

                ParserLog.Write(LogName,
                    $"Прогон завершён за {report.seconds}с | ключей {report.keysProcessed}, раздач {report.torrents}, " +
                    $"спросили {report.asked}, живых {report.alive}, нулей {report.zero}, молчали {report.unknown}, " +
                    $"воскресли {report.revived}, достигли порога {report.reachedThreshold}, удалено {report.deleted}");

                return report;
            }
            catch (OperationCanceledException)
            {
                report.note = "остановлено по времени, место запомнено";
                report.seconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
                ParserLog.Write(LogName, $"Прогон прерван по времени через {report.seconds}с");
                return report;
            }
            catch (Exception ex)
            {
                report.ok = false;
                report.note = ex.Message;
                ParserLog.Write(LogName, $"Ошибка: {ex.Message}");
                return report;
            }
            finally
            {
                _lock.Release();
            }
        }

        async Task<Dictionary<string, TrackerScrapeClient.Counts>> ScrapeAllAsync(
            Models.AppConf.SweepSettings conf,
            Dictionary<string, List<string>> announcesByHash,
            CancellationToken token)
        {
            // Группируем по трекерам: один трекер — много хешей за пакет.
            var byTracker = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in announcesByHash)
            {
                foreach (string a in pair.Value)
                {
                    if (!byTracker.TryGetValue(a, out var list))
                    {
                        list = new List<string>();
                        byTracker[a] = list;
                    }
                    list.Add(pair.Key);
                }
            }

            var result = new Dictionary<string, TrackerScrapeClient.Counts>(StringComparer.OrdinalIgnoreCase);
            var resultLock = new object();

            // Трекеры опрашиваем одновременно, а внутри одного — по очереди
            // с паузой. Пауза про вежливость к конкретному трекеру, и разным
            // трекерам она общей быть не должна: раньше из-за этого каждый
            // мёртвый анонс стоил целого таймаута всей очереди.
            int degree = Math.Max(1, conf.trackerConcurrency);
            var gate = new SemaphoreSlim(degree, degree);

            var tasks = byTracker
                .OrderByDescending(x => x.Value.Count)
                .Select(async pair =>
                {
                    string announce = pair.Key;
                    await gate.WaitAsync(token);

                    try
                    {
                        var unique = pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                        for (int i = 0; i < unique.Count; i += Math.Max(1, conf.maxHashesPerRequest))
                        {
                            token.ThrowIfCancellationRequested();

                            var chunk = unique.Skip(i).Take(Math.Max(1, conf.maxHashesPerRequest))
                                              .Select(FromHex).Where(b => b != null).ToList();
                            if (chunk.Count == 0)
                                continue;

                            var answer = await TrackerScrapeClient.ScrapeAsync(announce, chunk, conf.trackerTimeoutMs, token);

                            lock (resultLock)
                            {
                                foreach (var got in answer)
                                {
                                    // Раздачу могут знать несколько трекеров — берём лучший ответ.
                                    if (result.TryGetValue(got.Key, out var had) && had.Seeders >= got.Value.Seeders)
                                        continue;

                                    result[got.Key] = got.Value;
                                }
                            }

                            if (conf.delayMs > 0)
                                await Task.Delay(conf.delayMs, token);
                        }
                    }
                    finally
                    {
                        gate.Release();
                    }
                })
                .ToList();

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Вышли за отведённое время: отдаём то, что успели собрать.
                // Прежнее поведение было тем же, просто оно достигалось
                // прерыванием единственного цикла.
            }

            return result;
        }

        void ApplyResults(
            Models.AppConf.SweepSettings conf,
            Dictionary<string, List<(string url, string hash)>> byKey,
            Dictionary<string, TrackerScrapeClient.Counts> counts,
            SweepReport report)
        {
            foreach (var (key, items) in byKey)
            {
                bool touched = false;
                var toRemove = new List<string>();

                using (var fdb = FileDB.OpenWrite(key))
                {
                    if (fdb == null)
                        continue;

                    foreach (var (url, hash) in items)
                    {
                        if (!fdb.Database.TryGetValue(url, out TorrentDetails t) || t == null)
                            continue;

                        if (!counts.TryGetValue(hash, out var c))
                        {
                            // Ни один трекер не ответил про эту раздачу — это «не знаю».
                            report.unknown++;
                            continue;
                        }

                        t.lastAliveCheck = DateTime.UtcNow;

                        if (c.Seeders > 0)
                        {
                            report.alive++;
                            if (t.deadChecks > 0)
                            {
                                report.revived++;
                                t.deadChecks = 0;
                            }
                            t.sid = c.Seeders;
                            t.pir = c.Leechers;
                            touched = true;
                        }
                        else
                        {
                            report.zero++;
                            t.deadChecks++;
                            touched = true;

                            if (t.deadChecks >= Math.Max(1, conf.deadThreshold))
                            {
                                report.reachedThreshold++;
                                if (conf.deleteDead)
                                    toRemove.Add(url);
                            }
                        }
                    }

                    foreach (string url in toRemove)
                    {
                        fdb.Database.Remove(url);
                        report.deleted++;
                        touched = true;
                    }

                    if (fdb.Database.Count == 0)
                        FileDB.RemoveKeyFromMasterDb(key);

                    if (touched)
                        fdb.savechanges = true;
                }
            }
        }

        static string ReadCursor()
        {
            try
            {
                return File.Exists(CursorPath) ? File.ReadAllText(CursorPath).Trim() : null;
            }
            catch (IOException) { return null; }
        }

        static void SaveCursor(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CursorPath));
                File.WriteAllText(CursorPath, key);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Без закладки чистка каждый раз начинает базу заново и никогда
                // не доходит до конца — молчать об этом нельзя.
                JacRedLog.Swallowed(JacRedLogCategories.Fdb, "не записалась закладка чистки", ex);
            }
        }

        static string HashOf(string magnet)
        {
            try
            {
                var link = MagnetLink.Parse(magnet);
                return link.InfoHashes?.V1OrV2?.ToHex()?.ToLowerInvariant();
            }
            catch (FormatException) { return null; }
            catch (ArgumentException) { return null; }
        }

        static byte[] FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Length != 40)
                return null;
            try { return Convert.FromHexString(hex); }
            catch (FormatException) { return null; }
        }
    }
}
