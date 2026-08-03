using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Networking;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Infrastructure.Persistence;
using Newtonsoft.Json;

namespace JacBlack.Infrastructure.Trackers.Eztv
{
    /// <summary>
    /// Обход EZTV через их открытое API.
    ///
    /// Лента отдаётся страницами по 100 записей и отсортирована от свежих к
    /// старым, поэтому обычный обход берёт несколько первых страниц, а глубокий
    /// уходит дальше в историю.
    ///
    /// У глубины есть жёсткий потолок: примерно с сотой страницы API перестаёт
    /// листать и возвращает один и тот же кусок ленты. Замерено 30.07.2026 —
    /// страницы 100, 105 и 150 отдали идентичный набор. Поэтому доступно около
    /// десяти тысяч раздач, а не заявленный в ответе миллион. Обход, не знавший
    /// об этом, прошёл 884 страницы и не добавил ни одной новой записи, так что
    /// ниже стоит сторож: повторилась страница — дальше идти незачем.
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

        /// <summary>
        /// Отпечаток страницы: первый и последний идентификаторы плюс их число.
        /// Сравнивать целиком незачем — если границы окна совпали, это то же
        /// самое окно ленты.
        /// </summary>
        internal static string PageMark(EztvItem[] items) =>
            items == null || items.Length == 0
                ? null
                : $"{items[0].Id}:{items[items.Length - 1].Id}:{items.Length}";

        async Task<string> RunAsync(int fromPage, int pages, CancellationToken cancellationToken)
        {
            if (pages < 1)
                pages = 1;

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                var sw = Stopwatch.StartNew();
                ParserLog.Write(TrackerName, $"Parse start, страниц {pages}, host={Host}");

                int fetched = 0, accepted = 0, emptyInARow = 0;
                string previousPageMark = null;

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

                        // Потолок листания. За ним API отдаёт один и тот же
                        // кусок ленты, и обход крутится вхолостую часами.
                        string mark = PageMark(items);
                        if (mark != null && mark == previousPageMark)
                        {
                            ParserLog.Write(TrackerName, $"Страница {page} повторяет предыдущую — глубже лента не листается, обход завершён");
                            break;
                        }

                        previousPageMark = mark;
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
