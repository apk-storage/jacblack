using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Trackers.Eztv
{
    /// <summary>
    /// Обход EZTV через их открытое API.
    ///
    /// Лента отдаётся страницами по 100 записей и отсортирована от свежих к
    /// старым, поэтому обычный обход берёт несколько первых страниц, а глубокий
    /// уходит дальше в историю.
    ///
    /// Ограничение по частоте задаётся в настройках трекера; между страницами
    /// выдерживается пауза — источник открытый и бесплатный, добивать его
    /// незачем.
    /// </summary>
    public class EztvSyncService
    {
        const string TrackerName = "eztv";
        const int PageSize = 100;
        const int RequestDelayMs = 1500;

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        static string Host => (AppInit.conf.Eztv?.host ?? "https://eztvx.to").TrimEnd('/');

        public Task<string> ParseAsync(int pages = 3, CancellationToken cancellationToken = default) =>
            RunAsync(fromPage: 1, pages: pages, cancellationToken);

        /// <summary>Глубокий обход: уходит дальше в историю ленты.</summary>
        public Task<string> ParseAllAsync(int pages = 100, CancellationToken cancellationToken = default) =>
            RunAsync(fromPage: 1, pages: pages, cancellationToken);

        async Task<string> RunAsync(int fromPage, int pages, CancellationToken cancellationToken)
        {
            if (pages < 1)
                pages = 1;

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                var sw = Stopwatch.StartNew();
                ParserLog.Write(TrackerName, $"Parse start, страниц {pages}, host={Host}");

                int fetched = 0, accepted = 0, emptyInARow = 0;

                try
                {
                    for (int page = fromPage; page < fromPage + pages; page++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string url = $"{Host}/api/get-torrents?limit={PageSize}&page={page}";
                        string json = await HttpClient.Get(url, timeoutSeconds: 25, useproxy: AppInit.conf.Eztv.useproxy);

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            ParserLog.Write(TrackerName, $"Страница {page}: пустой ответ");
                            if (++emptyInARow >= 3)
                                break;

                            await Task.Delay(RequestDelayMs, cancellationToken);
                            continue;
                        }

                        EztvResponse response;
                        try { response = JsonConvert.DeserializeObject<EztvResponse>(json); }
                        catch (JsonException ex)
                        {
                            ParserLog.Write(TrackerName, $"Страница {page}: ответ не разобран — {ex.Message}");
                            continue;
                        }

                        var items = response?.Torrents;
                        if (items == null || items.Length == 0)
                        {
                            // Лента кончилась — дальше идти незачем.
                            ParserLog.Write(TrackerName, $"Страница {page}: записей нет, обход завершён");
                            break;
                        }

                        emptyInARow = 0;
                        fetched += items.Length;

                        var torrents = EztvParser.ParseItems(items);
                        if (torrents.Count > 0)
                        {
                            FileDB.AddOrUpdate(torrents);
                            accepted += torrents.Count;
                        }

                        ParserLog.Write(TrackerName, $"Страница {page} | в ответе {items.Length}, принято {torrents.Count}");

                        await Task.Delay(RequestDelayMs, cancellationToken);
                    }

                    string log = $"страниц={pages}, в ответах={fetched}, принято={accepted}";
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
