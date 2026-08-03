using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Networking;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Infrastructure.Persistence;
using Newtonsoft.Json;

namespace JacBlack.Infrastructure.Trackers.AnimeTosho
{
    /// <summary>
    /// Обход AnimeTosho через её JSON-ленту. Авторизация не нужна, magnet приходит
    /// прямо в ответе — поэтому, в отличие от Bitru, торрент-файл качать не требуется.
    /// </summary>
    public class AnimeToshoSyncService
    {
        const string TrackerName = "animetosho";
        const int RequestDelayMs = 700;
        const int MaxPages = 40;

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        static string Host => (AppInit.conf.AnimeTosho?.host ?? "https://feed.animetosho.org").TrimEnd('/');

        /// <summary>
        /// Обходит ленту постранично. parseFrom/parseTo — номера страниц ленты;
        /// при нулях берётся одна первая страница (75 записей).
        /// </summary>
        public async Task<string> ParseAsync(int parseFrom = 0, int parseTo = 0, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                int from = parseFrom > 0 ? parseFrom : 1;
                int to = parseTo > 0 ? parseTo : from;
                if (to < from) to = from;
                if (to - from + 1 > MaxPages) to = from + MaxPages - 1;

                var sw = Stopwatch.StartNew();
                ParserLog.Write(TrackerName, $"Parse start, pages {from}-{to}, host={Host}");

                int parsed = 0, saved = 0, pages = 0;

                try
                {
                    for (int page = from; page <= to; page++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var items = await FetchPage(page, cancellationToken);
                        if (items == null)
                        {
                            ParserLog.Write(TrackerName, $"Page {page}: пустой ответ, обход прерван");
                            break;
                        }

                        pages++;
                        parsed += items.Count;

                        var torrents = AnimeToshoParser.ParseTorrents(items);
                        if (torrents.Count > 0)
                        {
                            FileDB.AddOrUpdate(torrents);
                            saved += torrents.Count;
                        }

                        ParserLog.Write(TrackerName, $"Page {page} completed | parsed={items.Count}, accepted={torrents.Count}");

                        // Лента кончилась — дальше страницы пустые.
                        if (items.Count == 0)
                            break;

                        if (page < to)
                            await Task.Delay(RequestDelayMs, cancellationToken);
                    }

                    string log = $"pages={pages}, parsed={parsed}, saved={saved}";
                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s) | {log}");
                    return log;
                }
                catch (OperationCanceledException)
                {
                    ParserLog.Write(TrackerName, "Parse cancelled");
                    return "cancelled";
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                    return $"error: {ex.Message}";
                }
            });
        }

        /// <summary>Точечный дозабор по идентификатору тайтла в AniDB.</summary>
        public async Task<string> ParseByAnidbAsync(long aid, CancellationToken cancellationToken = default)
        {
            if (aid <= 0)
                return "bad aid";

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                try
                {
                    string url = $"{Host}/json?only_tor=1&aids={aid}";
                    var items = await RequestItems(url, cancellationToken);
                    if (items == null || items.Count == 0)
                        return "no items";

                    var torrents = AnimeToshoParser.ParseTorrents(items);
                    if (torrents.Count > 0)
                        FileDB.AddOrUpdate(torrents);

                    ParserLog.Write(TrackerName, $"ParseByAnidb aid={aid} | parsed={items.Count}, saved={torrents.Count}");
                    return $"saved {torrents.Count}";
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                    return $"error: {ex.Message}";
                }
            });
        }

        async Task<List<AnimeToshoItem>> FetchPage(int page, CancellationToken cancellationToken)
        {
            string url = page > 1
                ? $"{Host}/json?only_tor=1&page={page}"
                : $"{Host}/json?only_tor=1";

            return await RequestItems(url, cancellationToken);
        }

        async Task<List<AnimeToshoItem>> RequestItems(string url, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string json = await HttpClient.Get(
                url,
                timeoutSeconds: 20,
                useproxy: AppInit.conf.AnimeTosho?.useproxy ?? false);

            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<List<AnimeToshoItem>>(json) ?? new List<AnimeToshoItem>();
            }
            catch (JsonException ex)
            {
                ParserLog.Write(TrackerName, $"Разбор JSON не удался: {ex.Message}");
                return null;
            }
        }
    }
}
