using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Trackers.Yts
{
    /// <summary>
    /// Обход YTS через их открытое API.
    ///
    /// Про адрес. Основной yts.mx с нашей машины недоступен, работает зеркало
    /// yts.lt — оно перенаправляет на yts.gg. Сам сервис в ответе предупреждает,
    /// что базовый адрес переезжает на movies-api.accel.li. Поэтому хост вынесен
    /// в настройки, а запросы идут с переходом по перенаправлениям.
    ///
    /// Страницы отдаются по 50 записей и отсортированы от свежих к старым.
    /// Один фильм приносит два-четыре варианта качества — каждый становится
    /// отдельной раздачей.
    /// </summary>
    public class YtsSyncService
    {
        const string TrackerName = "yts";
        const int PageSize = 50;
        const int RequestDelayMs = 1500;

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        static string Host => (AppInit.conf.Yts?.host ?? "https://yts.lt").TrimEnd('/');

        public Task<string> ParseAsync(int pages = 4, CancellationToken cancellationToken = default) =>
            RunAsync(pages, cancellationToken);

        /// <summary>Глубокий обход: уходит дальше по каталогу.</summary>
        public Task<string> ParseAllAsync(int pages = 200, CancellationToken cancellationToken = default) =>
            RunAsync(pages, cancellationToken);

        async Task<string> RunAsync(int pages, CancellationToken cancellationToken)
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
                    for (int page = 1; page <= pages; page++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string url = $"{Host}/api/v2/list_movies.json?limit={PageSize}&page={page}";
                        string json = await HttpClient.Get(url, timeoutSeconds: 30, useproxy: AppInit.conf.Yts.useproxy);

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            ParserLog.Write(TrackerName, $"Страница {page}: пустой ответ");
                            if (++emptyInARow >= 3)
                                break;

                            await Task.Delay(RequestDelayMs, cancellationToken);
                            continue;
                        }

                        YtsResponse response;
                        try { response = JsonConvert.DeserializeObject<YtsResponse>(json); }
                        catch (JsonException ex)
                        {
                            ParserLog.Write(TrackerName, $"Страница {page}: ответ не разобран — {ex.Message}");
                            continue;
                        }

                        var movies = response?.Data?.Movies;
                        if (movies == null || movies.Length == 0)
                        {
                            ParserLog.Write(TrackerName, $"Страница {page}: фильмов нет, обход завершён");
                            break;
                        }

                        emptyInARow = 0;
                        fetched += movies.Length;

                        var torrents = YtsParser.ParseMovies(movies);
                        if (torrents.Count > 0)
                        {
                            FileDB.AddOrUpdate(torrents);
                            accepted += torrents.Count;
                        }

                        ParserLog.Write(TrackerName, $"Страница {page} | фильмов {movies.Length}, раздач принято {torrents.Count}");

                        await Task.Delay(RequestDelayMs, cancellationToken);
                    }

                    string log = $"страниц={pages}, фильмов={fetched}, принято={accepted}";
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

        /// <summary>
        /// Точечный поиск. Пригодится и для словаря кодов: по коду IMDB YTS
        /// находит фильм даже тогда, когда по названию промахивается.
        /// </summary>
        public async Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "нужен запрос";

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                string url = $"{Host}/api/v2/list_movies.json?limit={PageSize}&query_term={Uri.EscapeDataString(query)}";
                string json = await HttpClient.Get(url, timeoutSeconds: 30, useproxy: AppInit.conf.Yts.useproxy);

                if (string.IsNullOrWhiteSpace(json))
                    return "пустой ответ";

                var response = JsonConvert.DeserializeObject<YtsResponse>(json);
                var torrents = YtsParser.ParseMovies(response?.Data?.Movies);

                if (torrents.Count > 0)
                    FileDB.AddOrUpdate(torrents);

                int movies = response?.Data?.Movies?.Length ?? 0;
                ParserLog.Write(TrackerName, $"Поиск «{query}»: фильмов {movies}, принято {torrents.Count}");
                return $"найдено={movies}, принято={torrents.Count}";
            });
        }
    }
}
