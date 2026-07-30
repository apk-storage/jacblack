using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JacRed.Infrastructure.Networking
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
        /// <summary>Ответ похож на вызов Cloudflare, а не на обычный отказ.</summary>
        public static bool IsChallenge(HttpResponseMessage response)
        {
            if (response == null)
                return false;

            if (response.StatusCode != System.Net.HttpStatusCode.Forbidden &&
                response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable)
                return false;

            return response.Headers.TryGetValues("cf-mitigated", out _) ||
                   response.Headers.TryGetValues("cf-ray", out _);
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
            JacRedLog.Warning(JacRedLogCategories.Host, $"{host} закрыт проверкой Cloudflare, переходим на браузер");
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

                var solution = await RequestAsync(conf, url, cookie);

                // Отказ бывает не только от «сессии больше нет»: браузер может
                // упасть посреди решения задачи, и тогда служба отвечает
                // «Read timed out». Проверено 28.07.2026 — Chromium убивало
                // по памяти. На свежей сессии тот же адрес открывается.
                //
                // Поэтому вторая попытка делается при ЛЮБОМ отказе, но ровно
                // одна: если и она не прошла, значит дело не в браузере.
                if (solution == null)
                {
                    await DestroySessionAsync(conf);

                    if (!await CreateSessionAsync(conf))
                        return null;

                    solution = await RequestAsync(conf, url, cookie);

                    if (solution != null)
                        JacRedLog.Warning(JacRedLogCategories.Host, $"{host}: получилось со второй попытки, сессия пересоздана");
                }

                if (solution == null)
                    return null;

                _lastUse = DateTime.UtcNow;
                ArmIdleTimer(conf);

                return solution;
            }
            catch (Exception ex)
            {
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr: {host}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        static async Task<string> RequestAsync(FlareSolverrSettingsView conf, string url, string cookie)
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
            if (root == null)
                return null;

            if (!string.Equals(root.Value<string>("status"), "ok", StringComparison.OrdinalIgnoreCase))
            {
                string message = root.Value<string>("message") ?? "";
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr отказал: {message}");

                // Такое сообщение означает, что сессии больше нет.
                if (message.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0)
                    _sessionAlive = false;

                return null;
            }

            var solution = root.Value<JObject>("solution");
            int status = solution?.Value<int?>("status") ?? 0;
            string html = solution?.Value<string>("response");

            if (status != 200 || string.IsNullOrWhiteSpace(html))
                return null;

            return html;
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
                JacRedLog.Warning(JacRedLogCategories.Host, "FlareSolverr: сессия браузера создана");
            else
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr: сессию создать не удалось: {root?.Value<string>("message")}");

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

                JacRedLog.Warning(JacRedLogCategories.Host, "FlareSolverr: сессия закрыта по простою, память освобождена");
            }
            catch (Exception ex)
            {
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr: не удалось закрыть сессию: {ex.Message}");
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
                JacRedLog.Error(JacRedLogCategories.Host, $"FlareSolverr недоступен: {ex.GetType().Name}: {ex.Message}");
                _sessionAlive = false;
                return null;
            }
        }
    }
}
