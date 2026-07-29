using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Persistence;
using Newtonsoft.Json;

namespace JacRed.Infrastructure.Trackers.PirateBay
{
    /// <summary>
    /// Обход The Pirate Bay через открытое API apibay.org.
    ///
    /// Берём готовые списки «сотня самых раздаваемых» по каждому разделу:
    /// именно ради них источник и заводился. Замер 29.07.2026: в разделе
    /// HD-фильмов медиана 1268 сидов, максимум 10 140 — на два порядка больше
    /// типичных значений у русских трекеров.
    ///
    /// Поиск по запросу API тоже умеет, но для наполнения базы он хуже:
    /// свежие раздачи там почти без сидов, а нам нужны живые.
    /// </summary>
    public class PirateBaySyncService
    {
        const string TrackerName = "piratebay";
        const int RequestDelayMs = 1200;

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        static string Host => (AppInit.conf.PirateBay?.host ?? "https://apibay.org").TrimEnd('/');

        /// <summary>Разделы: фильмы, фильмы DVDR, сериалы, документальные, HD-фильмы, HD-сериалы, 3D.</summary>
        static readonly string[] Categories = { "207", "208", "201", "205", "202", "206", "209" };

        public async Task<string> ParseAsync(CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                var sw = Stopwatch.StartNew();
                ParserLog.Write(TrackerName, $"Parse start, разделов {Categories.Length}, host={Host}");

                int fetched = 0, accepted = 0;

                try
                {
                    foreach (string category in Categories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string url = $"{Host}/precompiled/data_top100_{category}.json";
                        string json = await HttpClient.Get(url, timeoutSeconds: 20, useproxy: AppInit.conf.PirateBay.useproxy);

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            ParserLog.Write(TrackerName, $"Раздел {category}: пустой ответ");
                            continue;
                        }

                        List<PirateBayItem> items;
                        try { items = JsonConvert.DeserializeObject<List<PirateBayItem>>(json); }
                        catch (JsonException ex)
                        {
                            ParserLog.Write(TrackerName, $"Раздел {category}: ответ не разобран — {ex.Message}");
                            continue;
                        }

                        if (items == null || items.Count == 0)
                            continue;

                        fetched += items.Count;

                        var torrents = PirateBayParser.ParseItems(items);
                        if (torrents.Count > 0)
                        {
                            FileDB.AddOrUpdate(torrents);
                            accepted += torrents.Count;
                        }

                        ParserLog.Write(TrackerName, $"Раздел {category} | в ответе {items.Count}, принято {torrents.Count}");

                        await Task.Delay(RequestDelayMs, cancellationToken);
                    }

                    string log = $"разделов={Categories.Length}, в ответах={fetched}, принято={accepted}";
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
        /// Точечный поиск по названию. Пригодится, когда в базе чего-то нет,
        /// а на TPB оно есть — например, свежий зарубежный релиз.
        /// </summary>
        public async Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "нужен запрос";

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                string url = $"{Host}/q.php?q={Uri.EscapeDataString(query)}&cat=200";
                string json = await HttpClient.Get(url, timeoutSeconds: 20, useproxy: AppInit.conf.PirateBay.useproxy);

                if (string.IsNullOrWhiteSpace(json))
                    return "пустой ответ";

                var items = JsonConvert.DeserializeObject<List<PirateBayItem>>(json);
                var torrents = PirateBayParser.ParseItems(items);

                if (torrents.Count > 0)
                    FileDB.AddOrUpdate(torrents);

                ParserLog.Write(TrackerName, $"Поиск «{query}»: в ответе {items?.Count ?? 0}, принято {torrents.Count}");
                return $"найдено={items?.Count ?? 0}, принято={torrents.Count}";
            });
        }
    }
}
