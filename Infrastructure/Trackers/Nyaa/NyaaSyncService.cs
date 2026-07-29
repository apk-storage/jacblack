using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;

namespace JacRed.Infrastructure.Trackers.Nyaa
{
    /// <summary>
    /// Обход nyaa.si через её ленту. Авторизация не нужна, хеш приходит прямо
    /// в ленте — ни torrent-файл качать, ни на страницу раздачи ходить не надо.
    ///
    /// Заведён 29.07.2026 взамен AnimeTosho: у того лента встала 8 мая, и это
    /// подтвердилось на самом сайте. AnimeTosho был витриной над nyaa, так что
    /// берём источник напрямую.
    /// </summary>
    public class NyaaSyncService
    {
        const string TrackerName = "nyaa";
        const int RequestDelayMs = 1500;
        const int MaxPages = 20;

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        static string Host => (AppInit.conf.Nyaa?.host ?? "https://nyaa.si").TrimEnd('/');

        /// <summary>
        /// Разделы аниме: 1_2 — с английскими субтитрами, 1_3 — без перевода,
        /// 1_4 — с другими субтитрами. Разделы вне аниме нам не нужны: их
        /// закрывают остальные трекеры, и они дали бы дубли.
        /// </summary>
        static readonly string[] Categories = { "1_2", "1_3", "1_4" };

        /// <summary>
        /// Обходит ленту по разделам. pages — сколько страниц брать в каждом
        /// (страница ленты это 75 записей).
        /// </summary>
        public async Task<string> ParseAsync(int pages = 1, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                int take = pages < 1 ? 1 : (pages > MaxPages ? MaxPages : pages);

                var sw = Stopwatch.StartNew();
                ParserLog.Write(TrackerName, $"Parse start, разделов {Categories.Length}, страниц по {take}, host={Host}");

                int fetched = 0, accepted = 0, requests = 0;

                try
                {
                    foreach (string cat in Categories)
                    {
                        for (int page = 1; page <= take; page++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string url = page > 1
                                ? $"{Host}/?page=rss&c={cat}&p={page}"
                                : $"{Host}/?page=rss&c={cat}";

                            string xml = await HttpClient.Get(url, timeoutSeconds: 20, useproxy: AppInit.conf.Nyaa.useproxy);
                            requests++;

                            if (string.IsNullOrWhiteSpace(xml))
                            {
                                ParserLog.Write(TrackerName, $"Раздел {cat}, страница {page}: пустой ответ");
                                break;
                            }

                            var items = NyaaParser.ParseFeed(xml);
                            fetched += items.Count;

                            var torrents = NyaaParser.ParseTorrents(items);
                            if (torrents.Count > 0)
                            {
                                FileDB.AddOrUpdate(torrents);
                                accepted += torrents.Count;
                            }

                            ParserLog.Write(TrackerName, $"Раздел {cat}, страница {page} | в ленте {items.Count}, принято {torrents.Count}");

                            // Лента кончилась — дальше страницы пустые.
                            if (items.Count == 0)
                                break;

                            await Task.Delay(RequestDelayMs, cancellationToken);
                        }
                    }

                    string log = $"запросов={requests}, в ленте={fetched}, принято={accepted}";
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
    }
}
