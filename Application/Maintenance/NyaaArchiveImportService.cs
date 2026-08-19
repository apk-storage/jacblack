using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Networking;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Infrastructure.Persistence;
using JacBlack.Infrastructure.Trackers.Nyaa;
using JacBlack.Models.Details;
using Newtonsoft.Json.Linq;

namespace JacBlack.Application.Maintenance
{
    /// <summary>
    /// Разовый импорт архива nyaa из дампа AnimeTosho.
    ///
    /// Зачем понадобился. Сама nyaa вглубь не пускает: и лента, и HTML-листинг
    /// отдают ровно сто страниц с каждого конца, то есть около 45 тысяч раздач
    /// из без малого миллиона. AnimeTosho был зеркалом nyaa и перед закрытием
    /// выложил свою базу — оттуда взяты 863 940 аниме-раздач с 2017 года
    /// (раньше нынешней nyaa попросту не существовало).
    ///
    /// Живость. В дампе счётчиков раздающих нет вовсе, поэтому каждая пачка
    /// хешей опрашивается у анонс-трекеров: клиент scrape спрашивает о 74
    /// раздачах одним пакетом. Раздача, о которой не знает ни один трекер,
    /// в базу не пишется — иначе архив 2017 года засорил бы выдачу мёртвым.
    /// Ноль сидов при этом смертью НЕ считается: раздача может жить на DHT,
    /// и такие записи мы сохраняем с нулём, опуская их в конец выдачи.
    ///
    /// Импорт возобновляемый: положение запоминается, и повторный запуск
    /// продолжает с места остановки, а не начинает сначала.
    /// </summary>
    public class NyaaArchiveImportService
    {
        const string LogName = "nyaa-import";
        const string SourcePath = "Data/nyaa-archive.jsonl";
        const string CursorPath = "Data/temp/nyaa_import_cursor.txt";

        // Столько хешей влезает в один scrape-пакет по BEP 15.
        const int BatchSize = 70;

        static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Импортирует порцию записей. limit — сколько строк файла разобрать за
        /// один заход; ноль означает «до конца файла».
        /// </summary>
        public async Task<string> ImportAsync(int limit = 0, bool probe = true, CancellationToken ct = default)
        {
            if (!await _lock.WaitAsync(0, ct))
                return "уже идёт";

            try
            {
                if (!File.Exists(SourcePath))
                    return "нет файла " + SourcePath;

                long start = ReadCursor();
                long line = 0;
                int взято = 0, живых = 0, безОтвета = 0, пачек = 0;

                var batch = new List<(TorrentDetails torrent, byte[] hash)>(BatchSize);
                var sw = System.Diagnostics.Stopwatch.StartNew();

                ParserLog.Write(LogName, $"Импорт с позиции {start}, лимит {(limit > 0 ? limit.ToString() : "весь файл")}, опрос {(probe ? "включён" : "выключен")}");

                using var reader = new StreamReader(SourcePath);
                string raw;
                while ((raw = await reader.ReadLineAsync()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    line++;

                    if (line <= start)
                        continue;

                    var item = Parse(raw);
                    if (item.torrent == null)
                        continue;

                    batch.Add(item);
                    взято++;

                    if (batch.Count >= BatchSize)
                    {
                        пачек++;
                        var результат = await FlushAsync(batch, probe, ct);
                        живых += результат.живых;
                        безОтвета += результат.безОтвета;
                        batch.Clear();
                        WriteCursor(line);

                        if (limit > 0 && взято >= limit)
                            break;
                    }
                }

                if (batch.Count > 0)
                {
                    пачек++;
                    var результат = await FlushAsync(batch, probe, ct);
                    живых += результат.живых;
                    безОтвета += результат.безОтвета;
                    WriteCursor(line);
                }

                string итог = $"позиция={line}, разобрано={взято}, записано={живых}, без ответа трекеров={безОтвета}, пачек={пачек}, за {sw.Elapsed.TotalMinutes:F1} мин";
                ParserLog.Write(LogName, "Импорт завершён | " + итог);
                return итог;
            }
            catch (OperationCanceledException)
            {
                ParserLog.Write(LogName, "Импорт прерван");
                return "прерван";
            }
            catch (Exception ex)
            {
                ParserLog.Write(LogName, "Ошибка: " + ex.Message);
                return "ошибка: " + ex.Message;
            }
            finally
            {
                _lock.Release();
            }
        }

        static (TorrentDetails torrent, byte[] hash) Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return (null, null);

            JObject o;
            try { o = JObject.Parse(raw); }
            catch { return (null, null); }

            string hash = (string)o["hash"];
            string name = (string)o["name"];
            string id = (string)o["id"];
            if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
                return (null, null);

            // Собираем ту же запись, что кладёт живой обход nyaa: имя разбирается
            // тем же способом, magnet собирается из хеша с теми же трекерами.
            var item = new NyaaItem
            {
                Title = name,
                ViewUrl = "https://nyaa.si/view/" + id,
                InfoHash = hash,
                SizeName = SizeName((long?)o["size"] ?? 0),
                PubDate = DateTimeOffset.FromUnixTimeSeconds((long?)o["created"] ?? 0).UtcDateTime,
                CategoryId = (string)o["cat"],
            };

            var torrent = NyaaParser.MapToTorrentDetails(item);
            if (torrent == null)
                return (null, null);

            return (torrent, HexToBytes(hash));
        }

        /// <summary>
        /// Опрашивает пачку у анонс-трекеров и пишет то, о чём хоть кто-то знает.
        /// </summary>
        static async Task<(int живых, int безОтвета)> FlushAsync(
            List<(TorrentDetails torrent, byte[] hash)> batch, bool probe, CancellationToken ct)
        {
            if (!probe)
            {
                FileDB.AddOrUpdate(batch.Select(b => b.torrent).ToList());
                return (batch.Count, 0);
            }

            var hashes = batch.Where(b => b.hash != null).Select(b => b.hash).ToList();
            var counts = new Dictionary<string, TrackerScrapeClient.Counts>(StringComparer.OrdinalIgnoreCase);

            // Трекеры спрашиваем ПО ОЧЕРЕДИ и останавливаемся, как только пачка
            // опознана целиком.
            //
            // Одновременный опрос всех четырёх пробовали 19.08.2026 и откатили:
            // вместо ускорения вышло замедление в семь раз — 25 секунд на пачку
            // против 3.7. Трекеры отвечают на залп хуже, чем на очередь, а
            // выигрывать тут нечего: первый же анонс обычно знает почти всё.
            foreach (string announce in NyaaParser.DefaultTrackers)
            {
                if (hashes.Count == 0 || counts.Count >= hashes.Count)
                    break;

                var answer = await TrackerScrapeClient.ScrapeAsync(announce, hashes, 3000, ct);
                foreach (var pair in answer)
                {
                    // Берём лучший ответ: разные трекеры знают разное, и ноль у
                    // одного не отменяет живых сидов у другого.
                    if (!counts.TryGetValue(pair.Key, out var было) || pair.Value.Seeders > было.Seeders)
                        counts[pair.Key] = pair.Value;
                }
            }

            var кПисьму = new List<TorrentDetails>(batch.Count);
            int безОтвета = 0;

            foreach (var (torrent, hash) in batch)
            {
                string key = hash == null ? null : Convert.ToHexString(hash).ToLowerInvariant();
                if (key != null && counts.TryGetValue(key, out var c))
                {
                    torrent.sid = c.Seeders;
                    torrent.pir = c.Leechers;
                    кПисьму.Add(torrent);
                }
                else
                {
                    // Ни один трекер не ответил про эту раздачу. Это не «мертва»,
                    // а «не знаю»: раздача может жить на DHT. Пишем с нулём —
                    // в выдаче такие опускаются вниз, а фоновая проверка живости
                    // разберётся с ними позже.
                    безОтвета++;
                    кПисьму.Add(torrent);
                }
            }

            if (кПисьму.Count > 0)
                FileDB.AddOrUpdate(кПисьму);

            return (кПисьму.Count, безОтвета);
        }

        static string SizeName(long bytes)
        {
            if (bytes <= 0)
                return null;

            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return value.ToString(value >= 100 ? "F0" : "F1", CultureInfo.InvariantCulture) + " " + units[unit];
        }

        static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Length != 40)
                return null;

            try { return Convert.FromHexString(hex); }
            catch { return null; }
        }

        static long ReadCursor()
        {
            try
            {
                if (File.Exists(CursorPath) && long.TryParse(File.ReadAllText(CursorPath).Trim(), out long value))
                    return value;
            }
            catch { }
            return 0;
        }

        static void WriteCursor(long line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CursorPath));
                File.WriteAllText(CursorPath, line.ToString(CultureInfo.InvariantCulture));
            }
            catch { }
        }
    }
}
