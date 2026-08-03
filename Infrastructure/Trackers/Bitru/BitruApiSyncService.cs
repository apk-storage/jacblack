using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Persistence;
using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Networking;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Models.Details;
using JacBlack.Models.tParse;
using Newtonsoft.Json;
using IO = System.IO;

namespace JacBlack.Infrastructure.Trackers.Bitru
{
    public class BitruApiSyncService
    {
        const string ApiGetTorrents = "torrents";
        const int ApiDelayMs = 250;
        const string TrackerName = "bitru";

        static readonly string ApiUrl;
        static readonly string HostUrl;
        static readonly string LastNewTorPath = "Data/temp/bitru_lastnewtor.txt";
        const string LegacyLastNewTorPath = "Data/temp/bitruapi_lastnewtor.txt";

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();

        /// <summary>Докуда дошёл полный обход. Продолжаем отсюда, а не с начала.</summary>
        const string DeepCursorPath = "Data/temp/bitru_deep_cursor.txt";

        /// <summary>С какой страницы списка продолжать обход архива.</summary>
        const string WebPagePath = "Data/temp/bitru_web_page.txt";

        static readonly SemaphoreSlim _loginGate = new SemaphoreSlim(1, 1);
        static string _cookie;

        /// <summary>
        /// Вход. Гостю bitru отдаёт 403 на browse.php при любой странице, а под
        /// учётной записью — 67 раздач и листание вглубь как минимум до
        /// двухтысячной страницы (замер 31.07.2026). То есть архив открывается
        /// только входом.
        ///
        /// Отдельно замечу, чтобы не искали заново: форма отправляется на
        /// takelogin.php, а не на login.php, и поле логина называется login,
        /// а не username, как у kinozal.
        /// </summary>
        async Task<string> CookieAsync()
        {
            if (!string.IsNullOrWhiteSpace(_cookie))
                return _cookie;

            var login = AppInit.conf.Bitru?.login;
            if (string.IsNullOrWhiteSpace(login?.u) || string.IsNullOrWhiteSpace(login?.p))
                return null;

            await _loginGate.WaitAsync();
            try
            {
                if (!string.IsNullOrWhiteSpace(_cookie))
                    return _cookie;

                _cookie = await TrackerLogin.TakeLoginAsync(TrackerName, HostUrl, "takelogin.php",
                    new Dictionary<string, string>
                    {
                        { "login", login.u },
                        { "password", login.p },
                        { "remember", "1" },
                        { "returnto", string.Empty }
                    });

                return _cookie;
            }
            finally
            {
                _loginGate.Release();
            }
        }

        static BitruApiSyncService()
        {
            var host = AppInit.conf.Bitru?.host?.TrimEnd('/') ?? "https://bitru.org";
            ApiUrl = $"{host}/api.php";
            HostUrl = host;
            MigrateLegacyLastNewTorFile();
        }

        static void MigrateLegacyLastNewTorFile()
        {
            try
            {
                if (IO.File.Exists(LastNewTorPath) || !IO.File.Exists(LegacyLastNewTorPath))
                    return;
                IO.File.Move(LegacyLastNewTorPath, LastNewTorPath);
            }
            catch (IO.IOException ex)
            {
                ParserLog.Write(TrackerName, $"Legacy lastnewtor migration failed: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                ParserLog.Write(TrackerName, $"Legacy lastnewtor migration failed: {ex.Message}");
            }
        }

        public async Task<string> ParseAsync(int limit = 100, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    ParserLog.Write(TrackerName, $"Parse start, limit={limit}, api={ApiUrl}");

                    var torrents = await FetchTorrentsFromApi(limit: Math.Min(100, limit), afterDateUnix: null, cancellationToken);
                    if (torrents != null && torrents.Count > 0)
                    {
                        await SaveTorrentsAndMagnets(torrents, cancellationToken);
                        log = $"saved {torrents.Count}";
                    }
                    else
                        log = "no items";

                    ParserLog.Write(TrackerName, $"Parse completed in {sw.Elapsed.TotalSeconds:F1}s, {log}");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                    log = $"error: {ex.Message}";
                }

                return string.IsNullOrWhiteSpace(log) ? "ok" : log;
            });
        }

        public async Task<string> ParseFromDateAsync(string lastnewtor, int limit = 100, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(lastnewtor))
                return "bad lastnewtor (use dd.MM.yyyy)";

            if (!DateTime.TryParseExact(lastnewtor.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fromDate))
                return "bad date format (use dd.MM.yyyy)";

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                string log = "";

                try
                {
                    var sw = Stopwatch.StartNew();
                    long unixFrom = BitruApiParser.UnixFromDate(fromDate);

                    ParserLog.Write(TrackerName, $"ParseFromDate lastnewtor={lastnewtor} (unix={unixFrom}), limit={limit}");

                    var torrents = await FetchTorrentsFromApi(limit: Math.Min(100, limit), afterDateUnix: unixFrom, cancellationToken);
                    if (torrents != null && torrents.Count > 0)
                    {
                        await SaveTorrentsAndMagnets(torrents, cancellationToken);
                        log = $"saved {torrents.Count}";
                    }
                    else
                        log = "no items";

                    ParserLog.Write(TrackerName, $"ParseFromDate completed in {sw.Elapsed.TotalSeconds:F1}s, {log}");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                    log = $"error: {ex.Message}";
                }

                return string.IsNullOrWhiteSpace(log) ? "ok" : log;
            });
        }

        /// <summary>
        /// Полный обход: идём по времени назад, пока раздачи не кончатся.
        ///
        /// Почему этого не было раньше и почему это дёшево. У bitru есть API,
        /// который отдаёт сотню раздач за запрос и курсор `before_date` на
        /// следующую сотню. Обычный проход брал только свежее и уходил максимум
        /// на 50 страниц — поэтому в базе лежало 48 844 записи против 147 543
        /// у старой. Полторы тысячи запросов по 250 мс — это минут шесть, то
        /// есть архив добирается за один прогон.
        ///
        /// Сохраняем пачками по мере получения, а не копим всё в памяти:
        /// полтора миллиона записей в списке нам ни к чему. Курсор пишем на
        /// диск, поэтому обрыв не начинает обход заново.
        /// </summary>
        public async Task<string> ParseAllTaskAsync(int maxPages = 5000, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAllTaskAsync(TrackerName, _parseAllTaskWork, checkDisabled: false, async () =>
            {
                var sw = Stopwatch.StartNew();
                long cursor = ReadDeepCursor();
                int pages = 0, saved = 0, empty = 0;

                ParserLog.Write(TrackerName, cursor > 0
                    ? $"глубокий обход: продолжаем с отметки {cursor}"
                    : "глубокий обход: начинаем со свежих");

                try
                {
                    while (pages < maxPages)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Delay(ApiDelayMs, cancellationToken);

                        var request = new Dictionary<string, object>
                        {
                            { "limit", 100 },
                            { "category", BitruCategories.RequestCategories }
                        };
                        if (cursor > 0)
                            request["before_date"] = cursor.ToString();

                        var resp = await ApiRequestAsync(request, cancellationToken);

                        if (resp == null || resp.HasError || resp.Result?.Items == null)
                        {
                            if (resp != null && resp.HasError && !string.IsNullOrEmpty(resp.ErrorMessage))
                                ParserLog.Write(TrackerName, $"глубокий обход: API ответил ошибкой — {resp.ErrorMessage}");
                            break;
                        }

                        pages++;

                        if (resp.Result.Items.Count == 0)
                        {
                            // Дошли до конца истории — курсор сбрасываем, чтобы
                            // следующий прогон начал заново со свежих.
                            ParserLog.Write(TrackerName, "глубокий обход: история кончилась");
                            SaveDeepCursor(0);
                            break;
                        }

                        var torrents = BitruApiParser.ParseTorrentsFromResponse(resp, HostUrl);
                        if (torrents.Count > 0)
                        {
                            await SaveTorrentsAndMagnets(torrents, cancellationToken);
                            saved += torrents.Count;
                            empty = 0;
                        }
                        else if (++empty >= 5)
                        {
                            // Пять страниц подряд без пригодных раздач — дальше
                            // ходить незачем, но и обрывать по одной рано.
                            ParserLog.Write(TrackerName, "глубокий обход: пять пустых страниц подряд, останавливаемся");
                            break;
                        }

                        long next = ToUnix(resp.Result.BeforeDate);
                        if (next == 0 || next == cursor)
                        {
                            ParserLog.Write(TrackerName, "глубокий обход: курсор перестал двигаться");
                            break;
                        }

                        cursor = next;
                        SaveDeepCursor(cursor);

                        if (pages % 50 == 0)
                            ParserLog.Write(TrackerName, $"глубокий обход: страниц {pages}, сохранено {saved}, идёт {sw.Elapsed.TotalMinutes:F0} мин");
                    }

                    string log = $"страниц={pages}, сохранено={saved}, заняло={sw.Elapsed.TotalMinutes:F1} мин";
                    ParserLog.Write(TrackerName, $"глубокий обход завершён | {log}");
                }
                catch (OperationCanceledException)
                {
                    ParserLog.Write(TrackerName, $"глубокий обход прерван на странице {pages}, курсор сохранён");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Полный обход архива по списку сайта.
        ///
        /// Почему не через API, хотя он есть. API отдаёт ровно две страницы:
        /// сотню свежих раздач и курсор, затем ещё девяносто девять — и курсор
        /// пропадает. Проверено 31.07.2026 и гостем, и под входом: это его
        /// устройство, а не ограничение для неавторизованных. Архив там взять
        /// нельзя, поэтому список берём с сайта.
        ///
        /// Устройство: browse.php отдаёт 65–67 раздач на страницу и листается
        /// вглубь (проверены страницы 0, 10, 100, 500, 1000, 2000). В списке
        /// только номера, поэтому карточка каждой раздачи забирается из API
        /// запросом {"id":N} — пачкой он не умеет, проверено.
        ///
        /// Место остановки пишется на диск, поэтому прерванный обход
        /// продолжается со своей страницы, а не начинается заново.
        /// </summary>
        public async Task<string> ParseArchiveAsync(int maxPages = 3000, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAllTaskAsync(TrackerName, _parseAllTaskWork, checkDisabled: false, async () =>
            {
                string cookie = await CookieAsync();
                if (string.IsNullOrWhiteSpace(cookie))
                {
                    ParserLog.Write(TrackerName, "обход архива: вход не выполнен, без него список закрыт (403)");
                    return;
                }

                var sw = Stopwatch.StartNew();
                int page = ReadNumber(WebPagePath);
                int pages = 0, seen = 0, saved = 0, emptyPages = 0;

                ParserLog.Write(TrackerName, $"обход архива: начинаем со страницы {page}");

                try
                {
                    while (pages < maxPages)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string html = await HttpClient.Get($"{HostUrl}/browse.php?page={page}",
                            cookie: cookie, timeoutSeconds: 20, useproxy: AppInit.conf.Bitru.useproxy);

                        if (string.IsNullOrWhiteSpace(html))
                        {
                            ParserLog.Write(TrackerName, $"обход архива: страница {page} не открылась, останавливаемся");
                            break;
                        }

                        var ids = ExtractIds(html);
                        pages++;
                        page++;

                        if (ids.Count == 0)
                        {
                            // Конец списка либо разовый сбой. Две подряд —
                            // считаем, что архив кончился.
                            if (++emptyPages >= 2)
                            {
                                ParserLog.Write(TrackerName, "обход архива: две пустые страницы подряд — дошли до конца");
                                SaveNumber(WebPagePath, 0);
                                break;
                            }
                            continue;
                        }

                        emptyPages = 0;
                        seen += ids.Count;

                        foreach (int id in ids)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await Task.Delay(ApiDelayMs, cancellationToken);

                            var resp = await ApiRequestAsync(new Dictionary<string, object> { { "id", id } }, cancellationToken);
                            if (resp?.Result?.Items == null || resp.HasError)
                                continue;

                            var torrents = BitruApiParser.ParseTorrentsFromResponse(resp, HostUrl);
                            if (torrents.Count == 0)
                                continue;

                            await SaveTorrentsAndMagnets(torrents, cancellationToken);
                            saved += torrents.Count;
                        }

                        SaveNumber(WebPagePath, page);

                        if (pages % 25 == 0)
                            ParserLog.Write(TrackerName, $"обход архива: страниц {pages}, раздач встречено {seen}, сохранено {saved}, идёт {sw.Elapsed.TotalMinutes:F0} мин");
                    }

                    ParserLog.Write(TrackerName, $"обход архива завершён | страниц={pages}, встречено={seen}, сохранено={saved}, заняло={sw.Elapsed.TotalMinutes:F1} мин");
                }
                catch (OperationCanceledException)
                {
                    ParserLog.Write(TrackerName, $"обход архива прерван на странице {page}, место сохранено");
                }
            }, cancellationToken);
        }

        /// <summary>Номера раздач из списка. В строке списка есть только они.</summary>
        static List<int> ExtractIds(string html)
        {
            var ids = new List<int>();
            var seen = new HashSet<int>();

            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(html, @"details\.php\?id=(\d+)"))
            {
                if (int.TryParse(m.Groups[1].Value, out int id) && seen.Add(id))
                    ids.Add(id);
            }

            return ids;
        }

        static int ReadNumber(string path)
        {
            try
            {
                return IO.File.Exists(path) && int.TryParse(IO.File.ReadAllText(path).Trim(), out int v) && v >= 0 ? v : 0;
            }
            catch (IO.IOException)
            {
                return 0;
            }
        }

        static void SaveNumber(string path, int value)
        {
            try
            {
                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(path));
                IO.File.WriteAllText(path, value.ToString());
            }
            catch (IO.IOException)
            {
                // Не записалось — следующий прогон начнёт с нуля. Повтор просто
                // обновит уже известное, потери данных нет.
            }
        }

        static long ToUnix(object value)
        {
            switch (value)
            {
                case long l: return l;
                case int i: return i;
                case string s when long.TryParse(s, out long parsed): return parsed;
                default: return 0;
            }
        }

        static long ReadDeepCursor()
        {
            try
            {
                if (!IO.File.Exists(DeepCursorPath))
                    return 0;
                return long.TryParse(IO.File.ReadAllText(DeepCursorPath).Trim(), out long v) ? v : 0;
            }
            catch (IO.IOException)
            {
                return 0;
            }
        }

        static void SaveDeepCursor(long value)
        {
            try
            {
                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(DeepCursorPath));
                IO.File.WriteAllText(DeepCursorPath, value.ToString());
            }
            catch (IO.IOException)
            {
                // Отметка не сохранилась — следующий прогон начнёт со свежих.
                // Неприятно, но не потеря: повтор просто обновит уже известное.
            }
        }

        /// <summary>
        /// Живые сиды по конкретным раздачам — через собственный API трекера.
        ///
        /// Почему у bitru иначе, чем у остальных. Опрос анонса ему не годится
        /// (торренты помечены private), поиска под входом с колонкой сидов у
        /// него нет, а листинг архива читается страницами по 67 раздач и в
        /// поиске карточки не поможет. Зато его API отдаёт seeders и leechers
        /// прямо в ответе на запрос по идентификатору — тем же путём, каким
        /// работает обход.
        ///
        /// Спрашиваем по одной раздаче, поэтому берём не больше десяти:
        /// в карточке их обычно одна-две, а вежливая пауза в четверть секунды
        /// между запросами превращает длинный список в секунды ожидания.
        /// Вызывается из фонового обновления, ответ никого не задерживает.
        /// </summary>
        public async Task<Dictionary<string, (int sid, int pir)>> LiveSeedersAsync(IReadOnlyList<int> ids)
        {
            var result = new Dictionary<string, (int sid, int pir)>(StringComparer.Ordinal);

            if (ids == null || ids.Count == 0)
                return result;

            string cookie = await CookieAsync();
            if (string.IsNullOrWhiteSpace(cookie))
                return result;

            foreach (int id in ids.Distinct().Take(10))
            {
                try
                {
                    await Task.Delay(ApiDelayMs);

                    var resp = await ApiRequestAsync(new Dictionary<string, object> { { "id", id } }, CancellationToken.None);
                    if (resp?.Result?.Items == null || resp.HasError)
                        continue;

                    // Ответ пришёл, ошибки нет, а раздачи в нём не оказалось —
                    // значит её на трекере больше нет. У bitru это признак
                    // однозначный, в отличие от «страница короткая»: ошибку
                    // связи мы отсекли выше проверкой на null и HasError.
                    if (resp.Result.Items.Count == 0)
                    {
                        Persistence.DeadReleases.Remember("bitru", id.ToString());
                        result[id.ToString()] = (0, 0);

                        JacBlackLog.Information(JacBlackLogCategories.Trackers,
                            $"bitru: раздача {id} на трекере не найдена, убрана из выдачи");
                        continue;
                    }

                    foreach (var t in BitruApiParser.ParseTorrentsFromResponse(resp, HostUrl))
                    {
                        var m = Regex.Match(t.url ?? string.Empty, @"details\.php\?id=(\d+)");
                        if (m.Success)
                            result[m.Groups[1].Value] = (t.sid, t.pir);
                    }
                }
                catch (Exception ex)
                {
                    JacBlackLog.Swallowed(JacBlackLogCategories.Trackers, $"bitru: живые сиды по раздаче {id} не получены", ex);
                }
            }

            return result;
        }

        async Task<BitruApiResponse> ApiRequestAsync(object jsonParams, CancellationToken cancellationToken)
        {
            string json = JsonConvert.SerializeObject(jsonParams);
            string postData = $"get={ApiGetTorrents}&json={Uri.EscapeDataString(json)}";
            cancellationToken.ThrowIfCancellationRequested();
            string response = await HttpClient.Post(ApiUrl, postData, timeoutSeconds: 15, useproxy: AppInit.conf.Bitru.useproxy);
            if (string.IsNullOrWhiteSpace(response))
                return null;

            return JsonConvert.DeserializeObject<BitruApiResponse>(response);
        }

        async Task<List<TorrentDetails>> FetchTorrentsFromApi(int limit, long? afterDateUnix, CancellationToken cancellationToken)
        {
            var all = new List<TorrentDetails>();
            var currentParams = new Dictionary<string, object>
            {
                { "limit", limit },
                { "category", BitruCategories.RequestCategories }
            };
            if (afterDateUnix.HasValue)
                currentParams["after_date"] = afterDateUnix.Value.ToString();

            for (int page = 0; page < 50; page++)
            {
                await Task.Delay(ApiDelayMs, cancellationToken);

                var resp = await ApiRequestAsync(currentParams, cancellationToken);
                if (resp == null || resp.HasError || resp.Result?.Items == null)
                {
                    if (resp != null && resp.HasError && !string.IsNullOrEmpty(resp.ErrorMessage))
                        ParserLog.Write(TrackerName, $"API error: {resp.ErrorMessage}");
                    break;
                }

                all.AddRange(BitruApiParser.ParseTorrentsFromResponse(resp, HostUrl));

                if (resp.Result.Items.Count == 0)
                    break;

                object nextDate = resp.Result.BeforeDate;
                if (nextDate == null)
                    break;

                long beforeUnix = 0;
                if (nextDate is long l)
                    beforeUnix = l;
                else if (nextDate is int i)
                    beforeUnix = i;
                else if (nextDate is string s && long.TryParse(s, out long parsed))
                    beforeUnix = parsed;

                if (beforeUnix == 0)
                    break;

                currentParams = new Dictionary<string, object>
                {
                    { "limit", limit },
                    { "category", BitruCategories.RequestCategories },
                    { "before_date", beforeUnix.ToString() }
                };
            }

            return all;
        }

        async Task SaveTorrentsAndMagnets(List<TorrentDetails> torrents, CancellationToken cancellationToken)
        {
            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                string downloadUrl = t._sn;
                if (string.IsNullOrWhiteSpace(downloadUrl) || !downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var idMatch = System.Text.RegularExpressions.Regex.Match(t.url ?? "", @"\?id=(\d+)");
                    downloadUrl = idMatch.Success ? $"{HostUrl}/api.php?download={idMatch.Groups[1].Value}" : null;
                }
                if (string.IsNullOrWhiteSpace(downloadUrl))
                    return false;

                await Task.Delay(ApiDelayMs, cancellationToken);

                byte[] data = await HttpClient.Download(downloadUrl, referer: HostUrl + "/", timeoutSeconds: 15, useproxy: AppInit.conf.Bitru.useproxy);
                string magnet = data != null ? BencodeTo.Magnet(data) : null;
                if (!string.IsNullOrWhiteSpace(magnet))
                {
                    t.magnet = magnet;
                    t._sn = null;
                    return true;
                }

                return false;
            });

            try
            {
                var lastTor = torrents.OrderByDescending(x => x.createTime).FirstOrDefault();
                if (lastTor != null)
                    IO.File.WriteAllText(LastNewTorPath, lastTor.createTime.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                // Отметка не сдвинется — следующий проход снова начнёт со старой даты
                // и молча перелопатит уже пройденное.
                JacBlackLog.Swallowed(JacBlackLogCategories.Trackers,
                    "bitru: не записалась отметка последней раздачи", ex);
            }
        }
    }
}
