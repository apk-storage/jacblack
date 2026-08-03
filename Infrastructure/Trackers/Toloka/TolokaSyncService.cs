using System;
using JacRed.Infrastructure.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;
using JacRed.Models.tParse;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacRed.Infrastructure.Trackers.Toloka
{
    public class TolokaSyncService
    {
        const string TrackerName = "toloka";

        readonly IMemoryCache _memoryCache;

        static Dictionary<string, List<TaskParse>> taskParse = new Dictionary<string, List<TaskParse>>();

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        static TolokaSyncService()
        {
            if (IO.File.Exists("Data/temp/toloka_taskParse.json"))
                taskParse = JsonConvert.DeserializeObject<Dictionary<string, List<TaskParse>>>(IO.File.ReadAllText("Data/temp/toloka_taskParse.json"));
        }

        public TolokaSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// Живые сиды по названию — через поиск самого трекера.
        ///
        /// Toloka вылезала в остатке у каждой карточки: живого опроса для неё
        /// не было вовсе, и её раздачи показывали снимок из базы. Замер
        /// 01.08.2026: у «Мандалорца» три непроверенных из 126 — все toloka.
        ///
        /// Гостю поиск закрыт, поэтому идём под входом — тем же, что и обход.
        /// Список несёт колонки сидов и пиров, один запрос на всё название.
        /// </summary>
        /// <summary>
        /// Пропала ли раздача. У toloka признак честный: пропавшей тема отдаёт
        /// 404, живая — 200 (проверено 03.08.2026). Спутать с отказом входа
        /// нельзя: форма входа приходит с кодом 200, а не 404.
        /// </summary>
        public async Task<bool?> IsDeletedAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            try
            {
                if (Cookie(_memoryCache) == null && !await TakeLogin(_memoryCache))
                    return null;

                var (_, response) = await HttpClient.BaseGetAsync(
                    $"{AppInit.conf.Toloka.host}/t{id}",
                    cookie: Cookie(_memoryCache),
                    timeoutSeconds: 12,
                    useproxy: AppInit.conf.Toloka.useproxy);

                return DeletedReleaseProbe.FromStatusCode(
                    response?.StatusCode ?? System.Net.HttpStatusCode.InternalServerError);
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Trackers, $"toloka: раздача t{id} не проверена", ex);
                return null;
            }
        }

        public async Task<Dictionary<string, (int sid, int pir)>> LiveSeedersAsync(string title)
        {
            var result = new Dictionary<string, (int sid, int pir)>(StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(title))
                return result;


            try
            {
                if (Cookie(_memoryCache) == null && !await TakeLogin(_memoryCache))
                    return result;

                string url = $"{AppInit.conf.Toloka.host}/tracker.php?nm={System.Web.HttpUtility.UrlEncode(title, Encoding.UTF8)}";
                string html = await HttpClient.Get(url, cookie: Cookie(_memoryCache), timeoutSeconds: 15, useproxy: AppInit.conf.Toloka.useproxy);

                if (string.IsNullOrEmpty(html))
                    return result;

                // Разметка страницы ПОИСКА отличается от форумного листинга:
                // класса topictitle там нет вовсе, из-за чего разбор молча
                // давал ноль на странице в 141 КБ (замер 01.08.2026). Поэтому
                // идём не по классам, а по строкам таблицы: в строке есть
                // ссылка на раздачу и её же счётчики. Так разбирается и та
                // разметка, и другая.
                var rows = Regex.Matches(html, @"<tr[^>]*>(?:(?!</tr>).)*</tr>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);

                foreach (Match row in rows)
                {
                    // Toloka адресует раздачи коротко и ОТНОСИТЕЛЬНО — в строке
                    // стоит href="t690003", без косой черты и без имени файла.
                    // Из-за косой черты в шаблоне разбор давал ноль на странице,
                    // где счётчиков было 17 (замер 01.08.2026). Длинную форму
                    // принимаем тоже: под ней лежат старые записи в базе.
                    var id = Regex.Match(row.Value, @"(?:viewtopic\.php\?t=|href=[""']/?t)(\d+)");
                    if (!id.Success)
                        continue;

                    var sm = Regex.Match(row.Value, @"seedmed[^>]*>\s*(?:<b>\s*)?(\d+)", RegexOptions.IgnoreCase);
                    var lm = Regex.Match(row.Value, @"leechmed[^>]*>\s*(?:<b>\s*)?(\d+)", RegexOptions.IgnoreCase);

                    if (!sm.Success)
                        continue;

                    int.TryParse(sm.Groups[1].Value, out int sid);
                    int.TryParse(lm.Success ? lm.Groups[1].Value : "0", out int pir);

                    result[id.Groups[1].Value] = (sid, pir);
                }

                if (result.Count == 0)
                    JacRedLog.Warning(JacRedLogCategories.Trackers,
                        $"toloka: поиск по «{title}» разобран в ноль ({html.Length} байт, строк {rows.Count}, " +
                        $"счётчиков {Regex.Matches(html, "seedmed", RegexOptions.IgnoreCase).Count}) — проверить разметку страницы поиска");
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Trackers, $"toloka: живые сиды по «{title}» не получены", ex);
            }

            return result;
        }

        static string Cookie(IMemoryCache memoryCache)
        {
            if (memoryCache.TryGetValue("cron:TolokaController:Cookie", out string cookie))
                return cookie;

            return null;
        }

        async static Task<bool> TakeLogin(IMemoryCache memoryCache)
        {
            try
            {
                var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false
                };

                clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                using (var client = new System.Net.Http.HttpClient(clientHandler))
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.MaxResponseContentBufferSize = 2000000; // 2MB
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");

                    var postParams = new Dictionary<string, string>
                    {
                        { "username", AppInit.conf.Toloka.login.u },
                        { "password", AppInit.conf.Toloka.login.p },
                        { "autologin", "on" },
                        { "ssl", "on" },
                        { "redirect", "index.php?" },
                        { "login", "Вхід" }
                    };

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"{AppInit.conf.Toloka.host}/login.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string toloka_sid = null, toloka_data = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("toloka_sid="))
                                        toloka_sid = new Regex("toloka_sid=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("toloka_data="))
                                        toloka_data = new Regex("toloka_data=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(toloka_sid) && !string.IsNullOrWhiteSpace(toloka_data))
                                {
                                    memoryCache.Set("cron:TolokaController:Cookie", $"toloka_sid={toloka_sid}; toloka_ssl=1; toloka_data={toloka_data};", DateTime.Now.AddHours(1));
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Parser, "toloka: вход не выполнен", ex);
            }

            return false;
        }

        public async Task<string> ParseAsync(int page)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    string baseUrl = AppInit.conf.Toloka.host;
                    ParserLog.Write(TrackerName, $"Starting parse page={page}, base: {baseUrl}");
                    foreach (string cat in new List<string>() { "16", "96", "19", "139", "32", "173", "174", "44" })
                    {
                        string pageUrl = page == 0 ? $"{baseUrl}/f{cat}" : $"{baseUrl}/f{cat}-{page * 45}?sort=8";
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
            #region Авторизация
            if (Cookie(_memoryCache) == null)
            {
                string authKey = "toloka:TakeLogin()";
                if (_memoryCache.TryGetValue(authKey, out _))
                {
                    IO.File.WriteAllText("Data/temp/toloka_taskParse.json", JsonConvert.SerializeObject(taskParse));
                    return "TakeLogin == null";
                }

                if (await TakeLogin(_memoryCache) == false)
                {
                    _memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(5));
                    IO.File.WriteAllText("Data/temp/toloka_taskParse.json", JsonConvert.SerializeObject(taskParse));
                    return "TakeLogin == null";
                }
            }
            #endregion

            foreach (string cat in new List<string>()
            {
                // Українське озвучення
                "16", "32",  "19", "44", "127",

                // Українське кіно
                "84", "42", "124", "125",

                // HD українською
                "96", "173", "139", "174", "140",

                // Документальні фільми українською
                "12", "131", "230", "226", "227", "228", "229",

                // Телевізійні шоу та програми
                "132"
            })
            {
                // Получаем html
                string html = await HttpClient.Get($"{AppInit.conf.Toloka.host}/f{cat}", timeoutSeconds: 10, cookie: Cookie(_memoryCache));
                if (html == null)
                    continue;

                // Максимальное количиство страниц
                int.TryParse(Regex.Match(html, ">([0-9]+)</a>&nbsp;&nbsp;<a href=\"[^\"]+\">наступна</a>").Groups[1].Value, out int maxpages);

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
                        JacRedLog.Swallowed(JacRedLogCategories.Parser, $"toloka: очередь для категории {cat} не построена", ex);
                    }
                }
            }

            IO.File.WriteAllText("Data/temp/toloka_taskParse.json", JsonConvert.SerializeObject(taskParse));
            return "ok";
        }

        /// <summary>Записывает очередь на диск: без этого ход обхода теряется при перезапуске.</summary>
        static void SaveTaskParse()
            => IO.File.WriteAllText("Data/temp/toloka_taskParse.json", JsonConvert.SerializeObject(taskParse));

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

                        await Task.Delay(AppInit.conf.Toloka.parseDelay);

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
                            await Task.Delay(AppInit.conf.Toloka.parseDelay);

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

        async Task<bool> parsePage(string cat, int page)
        {
            #region Авторизация
            if (Cookie(_memoryCache) == null)
            {
                string authKey = "toloka:TakeLogin()";
                if (_memoryCache.TryGetValue(authKey, out _))
                    return false;

                if (await TakeLogin(_memoryCache) == false)
                {
                    _memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(5));
                    return false;
                }
            }
            #endregion

            string html = await HttpClient.Get($"{AppInit.conf.Toloka.host}/f{cat}{(page == 0 ? "" : $"-{page * 45}")}?sort=8", cookie: Cookie(_memoryCache)/*, useproxy: true, proxy: tParse.webProxy()*/);
            if (html == null || !html.Contains("<html lang=\"uk\""))
                return false;

            var torrents = TolokaParser.ParseTorrentsFromPage(html, cat);

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                byte[] torrent = await HttpClient.Download($"{AppInit.conf.Toloka.host}/download.php?id={t.downloadId}", cookie: Cookie(_memoryCache), referer: AppInit.conf.Toloka.host);
                string magnet = BencodeTo.Magnet(torrent);
                if (magnet != null)
                {
                    t.magnet = magnet;
                    return true;
                }

                return false;
            });

            return torrents.Count > 0;
        }
    }
}
