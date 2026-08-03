using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JacBlack.Infrastructure.Networking
{
    /// <summary>
    /// Ходит на хосты, закрытые проверкой Cloudflare, через FlareSolverr —
    /// безголовый браузер, стоящий рядом в compose.
    ///
    /// Первым делом пробовали дешёвый путь: решить задачу браузером один раз,
    /// забрать cookie `cf_clearance` и дальше ходить обычным клиентом. Замер
    /// 27.07.2026 показал, что так нельзя — с той же cookie и тем же
    /// User-Agent обычный клиент получает 403. Cloudflare сверяет ещё и
    /// отпечаток TLS, а он у .NET другой, чем у браузера.
    ///
    /// Поэтому запросы к таким хостам идут через браузер целиком, но в ОДНОЙ
    /// постоянной сессии: задача решается один раз при её создании, дальше
    /// страницы отдаются быстро. Замер на rutracker: первая 80 с, следующие
    /// 2,4–3,0 с. Сессия держит около 700 МБ, поэтому простаивающая закрывается.
    /// </summary>
    public static class CloudflareClearance
    {
        const string SessionName = "jacblack";

        sealed class GuardState
        {
            public DateTime Since;

            /// <summary>Когда последний раз давали дешёвому пути шанс.</summary>
            public DateTime LastProbe;
        }

        /// <summary>Хосты, про которые уже известно, что они за проверкой: туда идём сразу браузером.</summary>
        static readonly ConcurrentDictionary<string, GuardState> _guarded = new(StringComparer.OrdinalIgnoreCase);

        // Браузер на машине один, и ядер всего два. Запросы к нему строго по очереди:
        // параллельные вызовы разгоняли нагрузку до 22 при двух ядрах.
        static readonly SemaphoreSlim _gate = new(1, 1);

        static bool _sessionAlive;
        static DateTime _lastUse = DateTime.MinValue;
        static Timer _idleTimer;

        static FlareSolverrSettingsView Conf
        {
            get
            {
                var c = AppInit.conf?.flaresolverr;

                // Выключено или не настроено — отдаём пустую запись,
                // у неё Url равен null, и все пути наверху это проверяют.
                return c == null || !c.enable || string.IsNullOrWhiteSpace(c.url)
                    ? default
                    : new FlareSolverrSettingsView(c);
            }
        }

        readonly struct FlareSolverrSettingsView
        {
            public readonly string Url;
            public readonly int MaxTimeoutMs;
            public readonly int SessionIdleMinutes;
            public readonly int GuardedHours;
            public readonly int RecheckMinutes;

            public FlareSolverrSettingsView(Models.AppConf.FlareSolverrSettings c)
            {
                Url = c.url;
                MaxTimeoutMs = c.maxTimeoutMs;
                SessionIdleMinutes = c.sessionIdleMinutes;
                GuardedHours = c.guardedHours;
                RecheckMinutes = c.recheckMinutes;
            }
        }

        #region признак «хост за проверкой»
        /// <summary>
        /// Ответ похож на вызов Cloudflare, а не на обычный отказ.
        ///
        /// Раньше признаком считалось «403 или 503 ПЛЮС заголовок cf-ray», и это
        /// было слишком широко: `cf-ray` Cloudflare ставит на КАЖДЫЙ ответ любого
        /// сайта за ним, включая обычный 200. Проверено 31.07.2026 на nnmclub.to —
        /// заголовок пришёл вместе с успешной страницей. Значит любой отказ
        /// самого трекера, отданный через Cloudflare (перегрузка, бан по частоте,
        /// профилактика), мы принимали за проверку и уводили хост в браузер.
        /// Именно так nnmclub застрял: глубокий обход упёрся в его же ограничение
        /// частоты, хост пометился закрытым, и дальше 99.6% страниц не открылись
        /// вовсе, хотя прямой запрос отдавал их целиком.
        ///
        /// Признаком остаётся `cf-mitigated` — его Cloudflare ставит именно
        /// тогда, когда сама вмешалась в запрос. Тело здесь недоступно, поэтому
        /// разбор разметки задачи делает <see cref="IsChallengeBody"/> у
        /// вызывающего, когда тело уже прочитано.
        /// </summary>
        public static bool IsChallenge(HttpResponseMessage response)
        {
            if (response == null)
                return false;

            if (response.StatusCode != System.Net.HttpStatusCode.Forbidden &&
                response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable)
                return false;

            return response.Headers.TryGetValues("cf-mitigated", out _);
        }

        /// <summary>
        /// Второй признак — по телу ответа. Нужен для старых видов проверки,
        /// где Cloudflare отдаёт страницу «Just a moment…» без `cf-mitigated`.
        /// Разметку задачи ни один трекер в обычной выдаче не отдаёт, поэтому
        /// ложное срабатывание маловероятно.
        /// </summary>
        public static bool IsChallengeBody(string body)
        {
            if (string.IsNullOrEmpty(body) || body.Length > 200_000)
                return false;

            return body.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase)
                || body.Contains("cf_chl_opt", StringComparison.OrdinalIgnoreCase)
                || body.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Про этот хост уже известно, что обычный клиент туда не пройдёт.</summary>
        public static bool IsGuarded(string host)
        {
            var conf = Conf;
            if (conf.Url == null || string.IsNullOrWhiteSpace(host))
                return false;

            if (!_guarded.TryGetValue(host, out var state))
                return false;

            var now = DateTime.UtcNow;

            // Отметка не вечная: защиту могут снять, и тогда дешёвый путь вернётся.
            if (now > state.Since.AddHours(conf.GuardedHours))
            {
                _guarded.TryRemove(host, out _);
                return false;
            }

            // Раз в recheckMinutes пропускаем один запрос обычным путём.
            // Без этого мы гоняли браузер все шесть часов подряд, даже если
            // трекер снял проверку через десять минут — так и случилось
            // 29.07.2026 с nnmclub: он давно отвечал 200, а мы всё ходили
            // через Chromium. Ошибиться тут дёшево: не пройдёт — вернёмся
            // к браузеру той же попыткой.
            if (now > state.LastProbe.AddMinutes(conf.RecheckMinutes))
            {
                state.LastProbe = now;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Хост ответил обычному клиенту — проверки на нём больше нет.
        ///
        /// Без этого отметка держалась все guardedHours, даже когда проба
        /// проходила успешно: IsGuarded пропускал один запрос обычным путём,
        /// но снять отметку было некому. Замерено 31.07.2026 на nnmclub —
        /// он давно отдавал 200 напрямую, а мы шесть часов гоняли его через
        /// браузер, где он делил одну сессию с rutracker и половина обращений
        /// отваливалась по таймауту: 221 из 450 за двадцать минут. Внешне это
        /// выглядело как «обход не работает» — в журнале ноль разобранных
        /// страниц и ни одной ошибки.
        /// </summary>
        public static void Unguard(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;

            if (_guarded.TryRemove(host, out _))
                JacBlackLog.Information(JacBlackLogCategories.Host, $"{host} отвечает обычному клиенту, браузер больше не нужен");
        }

        public static void MarkGuarded(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;

            var now = DateTime.UtcNow;

            if (_guarded.TryGetValue(host, out var state))
            {
                // Проба показала, что проверка на месте — отсчёт заново.
                state.Since = now;
                state.LastProbe = now;
                return;
            }

            _guarded[host] = new GuardState { Since = now, LastProbe = now };
            JacBlackLog.Warning(JacBlackLogCategories.Host, $"{host} закрыт проверкой Cloudflare, переходим на браузер");
        }

        #endregion

        #region получение страницы
        /// <summary>
        /// Забирает страницу через браузер. Возвращает готовый HTML либо null.
        /// Свой таймаут: решение задачи занимает до полутора минут, и таймаут
        /// вызывающего (обычно 15 с) для этого пути не годится.
        /// </summary>
        public static async Task<string> FetchAsync(string url, string cookie = null)
        {
            var conf = Conf;
            if (conf.Url == null || string.IsNullOrWhiteSpace(url))
                return null;

            string host;
            try { host = new Uri(url).Host; }
            catch (UriFormatException) { return null; }

            await _gate.WaitAsync();
            try
            {
                if (!_sessionAlive && !await CreateSessionAsync(conf))
                    return null;

                var (outcome, html) = await RequestAsync(conf, url, cookie);

                // Пересоздаём сессию ТОЛЬКО когда сломался браузер: он может
                // упасть посреди решения задачи, и служба отвечает «Read timed
                // out» — проверено 28.07.2026, Chromium убивало по памяти.
                //
                // А вот когда браузер отработал, но сайт ответил 403 или 404,
                // сессия ни при чём. Раньше её сносило и здесь, и каждая такая
                // страница стоила лишних 15 секунд на новое решение задачи.
                // Замер 31.07.2026: первая страница в новой сессии 15.5 с,
                // вторая и третья в той же — 2.7 и 2.3 с.
                if (outcome == FetchOutcome.BrowserFailed)
                {
                    await DestroySessionAsync(conf);

                    if (!await CreateSessionAsync(conf))
                        return null;

                    (outcome, html) = await RequestAsync(conf, url, cookie);

                    if (outcome == FetchOutcome.Ok)
                        JacBlackLog.Warning(JacBlackLogCategories.Host, $"{host}: получилось со второй попытки, сессия пересоздана");
                }

                // Сессия жива в любом случае, кроме уже обработанного выше:
                // отметку об использовании ставим и после неудачной страницы,
                // иначе полоса закрытых разделов усыпит браузер по простою.
                _lastUse = DateTime.UtcNow;
                ArmIdleTimer(conf);

                return outcome == FetchOutcome.Ok ? html : null;
            }
            catch (Exception ex)
            {
                JacBlackLog.Error(JacBlackLogCategories.Host, $"FlareSolverr: {host}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Чем кончилось обращение к браузеру. Разделение важное: раньше все
        /// три исхода выглядели как «пусто», и сессия сносилась даже тогда,
        /// когда сам браузер работал исправно, а сайт всего лишь ответил 403.
        /// Пересоздание стоит 15 секунд — замер 31.07.2026: первая страница
        /// в новой сессии 15.5 с, вторая и третья в той же 2.7 и 2.3 с.
        /// Отсюда и брались средние 50 секунд на страницу.
        /// </summary>
        enum FetchOutcome
        {
            /// <summary>Страница получена.</summary>
            Ok,

            /// <summary>Браузер отработал, но сайт не отдал страницу. Сессия цела.</summary>
            PageFailed,

            /// <summary>Сломался сам браузер или пропала сессия — нужна новая.</summary>
            BrowserFailed
        }

        /// <summary>
        /// Отправляет форму через браузер и возвращает страницу.
        ///
        /// Понадобилось для живых сидов rutracker: его поиск гостю не доступен,
        /// а под входом отдаёт список с колонкой сидов — 50 строк за один
        /// запрос. Обычным клиентом туда не пройти, там проверка Cloudflare,
        /// поэтому и вход, и поиск идут одной браузерной сессией.
        /// </summary>
        public static async Task<string> PostFormAsync(string url, string formData)
        {
            var conf = Conf;
            if (conf.Url == null || string.IsNullOrWhiteSpace(url))
                return null;

            await _gate.WaitAsync();
            try
            {
                if (!_sessionAlive && !await CreateSessionAsync(conf))
                    return null;

                var payload = new Dictionary<string, object>
                {
                    ["cmd"] = "request.post",
                    ["session"] = SessionName,
                    ["url"] = url,
                    ["postData"] = formData ?? string.Empty,
                    ["maxTimeout"] = conf.MaxTimeoutMs
                };

                var root = await CallAsync(conf, payload, conf.MaxTimeoutMs + 30000);
                if (root == null || !string.Equals(root.Value<string>("status"), "ok", StringComparison.OrdinalIgnoreCase))
                    return null;

                _lastUse = DateTime.UtcNow;
                return root["solution"]?.Value<string>("response");
            }
            catch (Exception ex)
            {
                JacBlackLog.Error(JacBlackLogCategories.Host, $"FlareSolverr: отправка формы не удалась: {ex.GetType().Name}");
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        static async Task<(FetchOutcome outcome, string html)> RequestAsync(FlareSolverrSettingsView conf, string url, string cookie)
        {
            var payload = new Dictionary<string, object>
            {
                ["cmd"] = "request.get",
                ["session"] = SessionName,
                ["url"] = url,
                ["maxTimeout"] = conf.MaxTimeoutMs
            };

            var jar = ParseCookies(cookie);
            if (jar.Count > 0)
                payload["cookies"] = jar;

            var root = await CallAsync(conf, payload, conf.MaxTimeoutMs + 30000);

            // До службы не достучались — это про браузер, не про страницу.
            if (root == null)
                return (FetchOutcome.BrowserFailed, null);

            if (!string.Equals(root.Value<string>("status"), "ok", StringComparison.OrdinalIgnoreCase))
            {
                string message = root.Value<string>("message") ?? "";
                JacBlackLog.Error(JacBlackLogCategories.Host, $"FlareSolverr отказал: {message}");

                // Такое сообщение означает, что сессии больше нет.
                if (message.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0)
                    _sessionAlive = false;

                return (FetchOutcome.BrowserFailed, null);
            }

            var solution = root.Value<JObject>("solution");
            int status = solution?.Value<int?>("status") ?? 0;
            string html = solution?.Value<string>("response");

            // Браузер отработал и принёс ответ сайта. Если сайт отдал 403 или
            // 404 — это свойство страницы, а не поломка сессии. Сносить её
            // здесь было главной причиной медленного обхода.
            if (status != 200 || string.IsNullOrWhiteSpace(html))
                return (FetchOutcome.PageFailed, null);

            return (FetchOutcome.Ok, html);
        }

        static List<Dictionary<string, string>> ParseCookies(string cookie)
        {
            var list = new List<Dictionary<string, string>>();
            if (string.IsNullOrWhiteSpace(cookie))
                return list;

            foreach (var part in cookie.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0)
                    continue;

                string name = part.Substring(0, eq).Trim();
                string value = part.Substring(eq + 1).Trim();

                if (name.Length > 0)
                    list.Add(new Dictionary<string, string> { ["name"] = name, ["value"] = value });
            }

            return list;
        }
        #endregion

        #region сессия
        static async Task<bool> CreateSessionAsync(FlareSolverrSettingsView conf)
        {
            var root = await CallAsync(conf, new Dictionary<string, object>
            {
                ["cmd"] = "sessions.create",
                ["session"] = SessionName
            }, conf.MaxTimeoutMs + 30000);

            bool ok = root != null &&
                      string.Equals(root.Value<string>("status"), "ok", StringComparison.OrdinalIgnoreCase);

            _sessionAlive = ok;

            if (ok)
                JacBlackLog.Warning(JacBlackLogCategories.Host, "FlareSolverr: сессия браузера создана");
            else
                JacBlackLog.Error(JacBlackLogCategories.Host, $"FlareSolverr: сессию создать не удалось: {root?.Value<string>("message")}");

            return ok;
        }

        /// <summary>Закрывает сессию, не поднимая шума: она могла уже умереть сама.</summary>
        static async Task DestroySessionAsync(FlareSolverrSettingsView conf)
        {
            if (!_sessionAlive)
                return;

            await CallAsync(conf, new Dictionary<string, object>
            {
                ["cmd"] = "sessions.destroy",
                ["session"] = SessionName
            }, 60000);

            _sessionAlive = false;
        }

        static void ArmIdleTimer(FlareSolverrSettingsView conf)
        {
            if (conf.SessionIdleMinutes <= 0)
                return;

            _idleTimer ??= new Timer(_ => CloseIfIdle(), null, Timeout.Infinite, Timeout.Infinite);

            var period = TimeSpan.FromMinutes(1);
            _idleTimer.Change(period, period);
        }

        /// <summary>Закрывает простаивающую сессию: браузер держит около 700 МБ.</summary>
        static void CloseIfIdle()
        {
            var conf = Conf;
            if (conf.Url == null || !_sessionAlive || conf.SessionIdleMinutes <= 0)
                return;

            if (DateTime.UtcNow < _lastUse.AddMinutes(conf.SessionIdleMinutes))
                return;

            // Если сейчас идёт запрос — не мешаем, закроем на следующем тике.
            if (!_gate.Wait(0))
                return;

            try
            {
                CallAsync(conf, new Dictionary<string, object>
                {
                    ["cmd"] = "sessions.destroy",
                    ["session"] = SessionName
                }, 60000).GetAwaiter().GetResult();

                _sessionAlive = false;
                _idleTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                JacBlackLog.Warning(JacBlackLogCategories.Host, "FlareSolverr: сессия закрыта по простою, память освобождена");
            }
            catch (Exception ex)
            {
                JacBlackLog.Error(JacBlackLogCategories.Host, $"FlareSolverr: не удалось закрыть сессию: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }
        #endregion

        static async Task<JObject> CallAsync(FlareSolverrSettingsView conf, Dictionary<string, object> payload, int timeoutMs)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
                using var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(conf.Url, content);

                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                JacBlackLog.Error(JacBlackLogCategories.Host, $"FlareSolverr недоступен: {ex.GetType().Name}: {ex.Message}");
                _sessionAlive = false;
                return null;
            }
        }
    }
}
