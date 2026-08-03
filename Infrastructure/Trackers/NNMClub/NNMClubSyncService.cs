using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;
using JacRed.Models.tParse;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.NNMClub
{
    public class NNMClubSyncService
    {
        const string TrackerName = "nnmclub";

        /// <summary>Portal page size; URL uses start={page * PageSize}.</summary>
        public const int PageSize = 25;

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static NNMClubSyncService()
        {
            if (IO.File.Exists("Data/temp/nnmclub_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/nnmclub_taskParse.json"));
        }

        public async Task<string> ParseAsync(int page)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = $"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php";
                    ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");

                    foreach (string cat in NNMClubCategories.Ids)
                    {
                        string pageUrl = $"{baseUrl}?c={cat}&start={page * PageSize}";
                        ParserLog.Write(TrackerName, $"Category {cat}: {pageUrl}");
                        await parsePage(cat, page);
                        log += $"{cat} - {page}\n";
                    }
                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                }

                return string.IsNullOrWhiteSpace(log) ? "ok" : log;
            });
        }

        public async Task<string> UpdateTasksParseAsync()
        {
            // After PageSize 20→25, regenerate taskParse via this endpoint so page indices match the portal.
            foreach (string cat in NNMClubCategories.Ids)
            {
                string html = await HttpClient.Get($"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php?c={cat}", encoding: Encoding.GetEncoding(1251), timeoutSeconds: 10, useproxy: AppInit.conf.NNMClub.useproxy);
                if (html == null || !html.Contains("NNM-Club"))
                    continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, "<a href=\"[^\"]+\">([0-9]+)</a>[^<\n\r]+<a href=\"[^\"]+\">След.</a>").Groups[1].Value, out int maxpages);

                // Загружаем список страниц в список задач
                for (int page = 0; page <= maxpages; page++)
                {
                    try
                    {
                        if (!taskParse.ContainsKey(cat))
                            taskParse.Add(cat, new List<TaskParse>());

                        var val = taskParse[cat];
                        if (val.FirstOrDefault(i => i.page == page) == null)
                            val.Add(new TaskParse(page));
                    }
                    catch (Exception ex)
                    {
                        // Страница просто не попадёт в задание — обход её пропустит
                        // и ничем этого не покажет. Поэтому пишем.
                        JacRedLog.Swallowed(JacRedLogCategories.Trackers,
                            $"nnmclub: страница {page} категории {cat} не попала в задание", ex);
                    }
                }
            }

            IO.File.WriteAllText("Data/temp/nnmclub_taskParse.json", JsonConvert.SerializeObject(taskParse));
            return "ok";
        }

        /// <summary>Записывает очередь на диск: без этого ход обхода теряется при перезапуске.</summary>
        static void SaveTaskParse()
            => IO.File.WriteAllText("Data/temp/nnmclub_taskParse.json", JsonConvert.SerializeObject(taskParse));

        public async Task<string> ParseAllTaskAsync()
        {
            return await TrackerSyncHelpers.RunParseAllTaskAsync(TrackerName, _parseAllTaskWork, checkDisabled: false, async () =>
            {
                var progress = new TrackerQueueProgress(TrackerName, SaveTaskParse,
                    taskParse.Sum(i => i.Value.Count));

                foreach (var task in taskParse.ToArray())
                {
                    foreach (var val in task.Value.ToArray())
                    {
                        if (DateTime.Today == val.updateTime)
                            continue;

                        await Task.Delay(AppInit.conf.NNMClub.parseDelay);

                        bool res = await parsePage(task.Key, val.page);
                        if (res)
                            val.updateTime = DateTime.Today;

                        progress.PageDone(res);
                    }
                }

                progress.Finish();
            });
        }

        public async Task<string> ParseLatestAsync(int pages = 5)
        {
            return await TrackerSyncHelpers.RunParseLatestAsync(TrackerName, _parseLatestLock, checkDisabled: false, async () =>
            {
                var log = new StringBuilder();

                try
                {
                    var sw = Stopwatch.StartNew();
                    ParserLog.Write(TrackerName, $"Starting ParseLatest pages={pages}");

                    foreach (var task in taskParse.ToArray())
                    {
                        var pagesToParse = task.Value.OrderBy(x => x.page).Take(pages).ToArray();

                        foreach (var val in pagesToParse)
                        {
                            await Task.Delay(AppInit.conf.NNMClub.parseDelay);

                            bool res = await parsePage(task.Key, val.page);
                            if (res)
                            {
                                val.updateTime = DateTime.Today;
                                log.AppendLine($"{task.Key} - {val.page}");
                            }
                        }
                    }

                    ParserLog.Write(TrackerName, $"ParseLatest completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"ParseLatest Error: {ex.Message}");
                }

                return log.ToString();
            });
        }

        #region обход архива через tracker.php
        /// <summary>
        /// Полный обход архива по форумам.
        ///
        /// Портал для этого не годится: у nnmclub потолок в 200 результатов на
        /// ЛЮБОЙ запрос, о чём страница сообщает прямо — «Результатов поиска:
        /// 200 (max: 200)». Поэтому portal.php со start=1000 отдаёт 302 на
        /// тему-заглушку, а глубокий обход в 11 380 страниц невозможен вовсе.
        ///
        /// Обходим дроблением: у tracker.php выбирается форум, а форумов 698.
        /// Каждый запрос упирается в свои 200, но 698 запросов дают на два
        /// порядка больше портала. Внутри форума берём обе сортировки по дате —
        /// свежие и старые, — то есть до 400 раздач с форума.
        ///
        /// Место остановки запоминается, поэтому прерванный обход продолжается,
        /// а не начинается заново.
        /// </summary>
        public async Task<string> ParseArchiveAsync(int maxForums = 1000)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                if (!_parseAllTaskWork.TryStart())
                    return "обход архива уже идёт";

                var sw = Stopwatch.StartNew();
                int forumsDone = 0, saved = 0, seen = 0;

                try
                {
                    var forums = await LoadForumIdsAsync();
                    if (forums.Count == 0)
                        return "список форумов получить не удалось";

                    int cursor = ReadArchiveCursor();
                    ParserLog.Write(TrackerName, $"обход архива: форумов {forums.Count}, продолжаю с {cursor}");

                    for (; cursor < forums.Count && forumsDone < maxForums; cursor++)
                    {
                        string forum = forums[cursor];
                        var batch = new List<TorrentBaseDetails>();
                        // Ссылки на торрент-файлы держим рядом: в модели раздачи
                        // такого поля нет, а предикат сохранения получает только её.
                        var downloadUrls = new Dictionary<string, string>();

                        // Две сортировки по дате регистрации: свежие и старые.
                        // Потолок в 200 срабатывает на каждую выборку отдельно,
                        // так что с форума снимается до 400 раздач вместо 200.
                        foreach (string order in new[] { "2", "1" })
                        {
                            for (int start = 0; start < NNMClubTrackerParser.ResultsCap; start += NNMClubTrackerParser.PageSize)
                            {
                                string url = $"{AppInit.conf.NNMClub.rqHost()}/forum/tracker.php?f%5B%5D={forum}&o=1&s={order}&start={start}";
                                string html = await HttpClient.Get(url, encoding: Encoding.GetEncoding(1251), timeoutSeconds: 20, useproxy: AppInit.conf.NNMClub.useproxy);

                                var rows = NNMClubTrackerParser.Parse(html);
                                if (rows.Count == 0)
                                    break;

                                seen += rows.Count;
                                foreach (var row in rows)
                                {
                                    var t = ToTorrent(row);
                                    if (t == null || downloadUrls.ContainsKey(t.url))
                                        continue;

                                    batch.Add(t);
                                    downloadUrls[t.url] = string.IsNullOrEmpty(row.DownloadId)
                                        ? null
                                        : $"{AppInit.conf.NNMClub.host}/forum/download.php?id={row.DownloadId}";
                                }
                            }
                        }

                        if (batch.Count > 0)
                        {
                            await FileDB.AddOrUpdate(batch, async (t, db) =>
                            {
                                // Уже знаем эту раздачу — за торрент-файлом не идём.
                                // Без этой проверки каждый проход выкачивал бы
                                // сотню тысяч файлов заново.
                                if (db.TryGetValue(t.url, out TorrentDetails cached) && !string.IsNullOrEmpty(cached.magnet))
                                {
                                    t.magnet = cached.magnet;
                                    return true;
                                }

                                downloadUrls.TryGetValue(t.url, out string dl);
                                string magnet = await MagnetFromTorrentFileAsync(dl, t.url);
                                if (string.IsNullOrEmpty(magnet))
                                    return false;

                                t.magnet = magnet;
                                return true;
                            });

                            saved += batch.Count;
                        }

                        forumsDone++;
                        WriteArchiveCursor(cursor + 1);

                        if (forumsDone % 20 == 0)
                            ParserLog.Write(TrackerName, $"обход архива: форумов {forumsDone} из {forums.Count}, встречено {seen}, сохранено {saved}, идёт {sw.Elapsed.TotalMinutes:F0} мин");
                    }

                    if (cursor >= forums.Count)
                    {
                        WriteArchiveCursor(0);
                        ParserLog.Write(TrackerName, "обход архива: круг завершён, следующий начнётся сначала");
                    }
                }
                catch (Exception ex)
                {
                    JacRedLog.Swallowed(JacRedLogCategories.Trackers, "nnmclub: обход архива прерван", ex);
                }
                finally
                {
                    _parseAllTaskWork.End();
                }

                string result = $"форумов {forumsDone}, встречено {seen}, сохранено {saved}, заняло {sw.Elapsed.TotalMinutes:F1} мин";
                ParserLog.Write(TrackerName, $"ИТОГ архива | {result}");
                return result;
            });
        }

        /// <summary>
        /// Список форумов берётся из самой формы поиска: там перечислены все
        /// разделы. Держим его в файле, чтобы не тянуть страницу на каждый заход,
        /// но и не зашиваем в код — разделы появляются и исчезают.
        /// </summary>
        async Task<List<string>> LoadForumIdsAsync()
        {
            const string path = "Data/temp/nnmclub_forums.json";

            try
            {
                if (IO.File.Exists(path) && IO.File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddDays(-7))
                {
                    var cached = JsonConvert.DeserializeObject<List<string>>(IO.File.ReadAllText(path));
                    if (cached != null && cached.Count > 0)
                        return cached;
                }
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Trackers, "nnmclub: список форумов из файла не прочитался", ex);
            }

            string html = await HttpClient.Get($"{AppInit.conf.NNMClub.rqHost()}/forum/tracker.php", encoding: Encoding.GetEncoding(1251), timeoutSeconds: 25, useproxy: AppInit.conf.NNMClub.useproxy);
            var ids = new List<string>();

            if (!string.IsNullOrWhiteSpace(html))
            {
                var select = Regex.Match(html, "<select[^>]*name=\"f\\[\\]\"[^>]*>(.*?)</select>", RegexOptions.Singleline);
                if (select.Success)
                {
                    foreach (Match m in Regex.Matches(select.Groups[1].Value, "value=\"(\\d+)\""))
                        ids.Add(m.Groups[1].Value);
                }
            }

            if (ids.Count > 0)
            {
                try
                {
                    IO.Directory.CreateDirectory("Data/temp");
                    IO.File.WriteAllText(path, JsonConvert.SerializeObject(ids));
                }
                catch (Exception ex)
                {
                    JacRedLog.Swallowed(JacRedLogCategories.Trackers, "nnmclub: список форумов не сохранился", ex);
                }
            }

            ParserLog.Write(TrackerName, $"форумов в списке: {ids.Count}");
            return ids;
        }

        /// <summary>
        /// В выдаче tracker.php нет ни magnet, ни хеша — только ссылка на
        /// торрент-файл. Забираем файл и берём хеш из него.
        /// </summary>
        async Task<string> MagnetFromTorrentFileAsync(string downloadUrl, string topicUrl)
        {
            if (string.IsNullOrEmpty(downloadUrl))
                return null;

            try
            {
                byte[] data = await HttpClient.Download(downloadUrl, timeoutSeconds: 25, useproxy: AppInit.conf.NNMClub.useproxy);
                if (data == null || data.Length < 64)
                    return null;

                var torrent = MonoTorrent.Torrent.Load(data);
                string hash = torrent?.InfoHashes?.V1OrV2?.ToHex();

                return string.IsNullOrEmpty(hash) ? null : $"magnet:?xt=urn:btih:{hash}";
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Trackers, $"nnmclub: торрент-файл не разобран ({topicUrl})", ex);
                return null;
            }
        }

        static TorrentBaseDetails ToTorrent(NNMClubTrackerParser.Row row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.Title) || string.IsNullOrEmpty(row.TopicId))
                return null;

            if (row.Title.ToLower().Contains("трейлер"))
                return null;

            var (types, kind) = NNMClubCategories.ForForumName(row.ForumName);

            // Заголовок разбираем тем же правилом, что и портал: иначе одна
            // раздача, пришедшая двумя путями, легла бы в базу под разными
            // ключами — она шардируется по имени.
            NNMClubParser.ParseTitleNames(kind, row.Title, out string name, out string originalname, out int relased);

            if (string.IsNullOrWhiteSpace(name))
                name = Regex.Split(row.Title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

            if (string.IsNullOrWhiteSpace(name))
                return null;

            return new TorrentBaseDetails
            {
                trackerName = TrackerName,
                types = types,
                url = $"{AppInit.conf.NNMClub.host}/forum/viewtopic.php?t={row.TopicId}",
                title = row.Title,
                sid = row.Sid,
                pir = row.Pir,
                sizeName = row.SizeName,
                createTime = row.CreateTime == default ? DateTime.UtcNow : row.CreateTime,
                name = name,
                originalname = originalname,
                relased = relased
            };
        }

        static int ReadArchiveCursor()
        {
            try
            {
                string path = "Data/temp/nnmclub_archive_cursor.txt";
                if (IO.File.Exists(path) && int.TryParse(IO.File.ReadAllText(path).Trim(), out int v) && v >= 0)
                    return v;
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Trackers, "nnmclub: место остановки не прочиталось", ex);
            }

            return 0;
        }

        static void WriteArchiveCursor(int value)
        {
            try
            {
                IO.Directory.CreateDirectory("Data/temp");
                IO.File.WriteAllText("Data/temp/nnmclub_archive_cursor.txt", value.ToString());
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Trackers, "nnmclub: место остановки не сохранилось", ex);
            }
        }
        #endregion

        async Task<bool> parsePage(string cat, int page)
        {
            string html = await HttpClient.Get($"{AppInit.conf.NNMClub.rqHost()}/forum/portal.php?c={cat}&start={page * PageSize}", encoding: Encoding.GetEncoding(1251), useproxy: AppInit.conf.NNMClub.useproxy);
            if (html == null || !html.Contains("NNM-Club"))
                return false;

            var torrents = NNMClubParser.ParseTorrentsFromPage(html, cat);

            FileDB.AddOrUpdate(torrents);
            return torrents.Count > 0;
        }
    }
}
