using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace JacBlack.Infrastructure.Networking
{
    /// <summary>
    /// Быстрый путь к хостам за проверкой Cloudflare.
    ///
    /// Разделение труда простое: браузер решает задачу и отдаёт cookie
    /// `cf_clearance`, а страницы дальше берёт обычный HTTP-клиент — но с
    /// отпечатком TLS настоящего Chrome, иначе та же cookie не работает.
    /// Отпечаток подменяет сторонний контейнер `cffetch`, в .NET рукопожатие
    /// TLS не настраивается.
    ///
    /// Замер 07.09.2026 на rutracker: страница за 0.13 с против 3.9 с через
    /// браузер. Это не мелкая экономия, а разница между «обход идёт» и
    /// «обход стоит»: браузер один на всех, и при 947 обращениях в час он
    /// занят на 101%, отчего глубокий обход получал две страницы в час из
    /// 947 и продвигался на одну страницу за 72 минуты.
    ///
    /// Почему раньше не вышло. 27.07.2026 ровно это пробовали клиентом .NET
    /// и получили 403 — у него свой отпечаток. Вывод «через cookie нельзя»
    /// был неверен: нельзя было именно с ЧУЖИМ отпечатком.
    /// </summary>
    public static class CfFetch
    {
        /// <summary>Что браузер добыл для хоста: cookie и то, чем он представлялся.</summary>
        public sealed class Clearance
        {
            public string Cookies { get; init; }
            public string UserAgent { get; init; }
            public DateTime At { get; init; }
        }

        static readonly ConcurrentDictionary<string, Clearance> _clearance =
            new(StringComparer.OrdinalIgnoreCase);

        static SemaphoreSlim _gate;
        static int _gateSize;

        static Models.AppConf.CfFetchSettings Conf
        {
            get
            {
                var c = AppInit.conf?.cffetch;
                return c == null || !c.enable || string.IsNullOrWhiteSpace(c.url) ? null : c;
            }
        }

        #region cookie от браузера
        /// <summary>
        /// Запомнить, с чем браузер прошёл. Зовётся после КАЖДОГО удачного
        /// обращения через FlareSolverr: cookie обновляются сами собой, в том
        /// числе после входа на трекер, и брать надо свежие.
        /// </summary>
        public static void Remember(string host, string cookies, string userAgent)
        {
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(cookies))
                return;

            _clearance.TryGetValue(host, out var had);

            // Складываем со старым набором, а не заменяем его.
            //
            // Иначе теряется главное. Браузер отдаёт cookie того запроса,
            // который делал: у входа на трекер это его собственные cookie, и
            // `cf_clearance` в них может не прийти вовсе. Замена стирала
            // рабочую clearance, следующий запрос получал 403, мы шли в
            // браузер — и так по кругу. Замер 07.09.2026: 46 «потерь cookie»
            // за пять минут при том, что снятая двумя часами раньше clearance
            // всё ещё открывала страницы.
            string merged = Merge(had?.Cookies, cookies);

            _clearance[host] = new Clearance
            {
                Cookies = merged,
                UserAgent = string.IsNullOrWhiteSpace(userAgent) ? had?.UserAgent : userAgent,
                At = DateTime.UtcNow
            };

            if (had == null)
                JacBlackLog.Warning(JacBlackLogCategories.Host, $"{host}: cookie от браузера получена, дальше идём быстрым путём");
        }

        /// <summary>
        /// Складывает два набора cookie: при совпадении имени побеждает новый,
        /// остальное из старого сохраняется.
        /// </summary>
        static string Merge(string old, string fresh)
        {
            if (string.IsNullOrWhiteSpace(old))
                return fresh;

            var jar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in new[] { old, fresh })
            {
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                foreach (var part in source.Split(';'))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string name = part.Substring(0, eq).Trim();
                    if (name.Length > 0)
                        jar[name] = part.Substring(eq + 1).Trim();
                }
            }

            var sb = new StringBuilder();
            foreach (var pair in jar)
            {
                if (sb.Length > 0)
                    sb.Append("; ");

                sb.Append(pair.Key).Append('=').Append(pair.Value);
            }

            return sb.ToString();
        }

        #region когда быстрый путь недоступен вовсе
        static readonly ConcurrentDictionary<string, DateTime> _blocked =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// На сколько минут забыть про быстрый путь у хоста, когда cookie от
        /// браузера ему не годится. Проверять чаще смысла нет: это решение
        /// Cloudflare, а не сбой.
        /// </summary>
        const int BlockedMinutes = 30;

        /// <summary>
        /// Cookie от браузера не подошла — быстрого пути у этого хоста пока
        /// нет, ходим браузером.
        ///
        /// Так 07.09.2026 повёл себя rutracker: cookie, снятая в 18:10, весь
        /// вечер открывала страницы помощнику, а выданная в 20:50 — сразу
        /// 403 с `cf-mitigated`, причём даже после настоящего «Challenge
        /// solved!» и при любом из семи отпечатков. То есть Cloudflare
        /// ужесточил привязку, и новые clearance годятся только самому
        /// браузеру. Без этой отметки служба крутила петлю: помощник ловил
        /// отказ, шла в браузер, получала негодную cookie, снова отказ —
        /// 133 «потери» и 134 новых cookie за пять минут.
        /// </summary>
        public static void BlockFastPath(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;

            var until = DateTime.UtcNow.AddMinutes(BlockedMinutes);

            bool first = !_blocked.ContainsKey(host) || _blocked[host] < DateTime.UtcNow;
            _blocked[host] = until;

            if (first)
                JacBlackLog.Warning(JacBlackLogCategories.Host,
                    $"{host}: cookie от браузера помощнику не годится, {BlockedMinutes} мин ходим браузером");
        }

        public static bool FastPathBlocked(string host) =>
            !string.IsNullOrWhiteSpace(host)
            && _blocked.TryGetValue(host, out var until)
            && DateTime.UtcNow < until;
        #endregion

        /// <summary>Годная cookie для хоста либо null.</summary>
        public static Clearance For(string host)
        {
            var conf = Conf;
            if (conf == null || string.IsNullOrWhiteSpace(host))
                return null;

            if (FastPathBlocked(host))
                return null;

            if (!_clearance.TryGetValue(host, out var c))
                return null;

            if (conf.clearanceMinutes > 0 && DateTime.UtcNow > c.At.AddMinutes(conf.clearanceMinutes))
                return null;

            return c;
        }

        /// <summary>
        /// Забыть cookie: она больше не проходит. Следующий запрос пойдёт
        /// через браузер, тот решит задачу заново и положит сюда свежую.
        /// </summary>
        public static void Forget(string host)
        {
            if (!string.IsNullOrWhiteSpace(host) && _clearance.TryRemove(host, out _))
                JacBlackLog.Warning(JacBlackLogCategories.Host, $"{host}: cookie больше не проходит, возвращаемся к браузеру");
        }

        /// <summary>Для тестов и диагностики.</summary>
        internal static void Reset()
        {
            _clearance.Clear();
            _mitigated.Clear();
            _blocked.Clear();
        }

        /// <summary>
        /// Проверяет cookie делом, прежде чем ей доверять: тем же помощником
        /// и на той же странице, которую только что взял браузер.
        ///
        /// Нужно потому, что «браузер прошёл» больше не означает «пройдём и
        /// мы»: 07.09.2026 Cloudflare начал привязывать свежие clearance к
        /// отпечатку самого браузера. Без проверки служба принимала негодную
        /// cookie за рабочую и уходила в петлю.
        /// </summary>
        public static async Task<bool> ValidateAsync(string url, Clearance candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(url))
                return false;

            var (status, body, mitigated) = await GetAsync(url, candidate);

            // До помощника не достучались — не повод объявлять cookie плохой.
            if (status == 0)
                return true;

            return !ClearanceLost(status, body, mitigated);
        }
        #endregion

        #region терпимость к одиночным отказам
        sealed class MitigationRun
        {
            public DateTime Since;
            public int Count;
        }

        static readonly ConcurrentDictionary<string, MitigationRun> _mitigated =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Сколько отказов подряд считать доказательством, что cookie мертва.</summary>
        const int MitigationsToDrop = 3;

        /// <summary>За какое время они должны прийти.</summary>
        static readonly TimeSpan MitigationWindow = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Cloudflare вмешалась в один запрос. Возвращает true, только если
        /// это уже не случайность и cookie пора выбрасывать.
        ///
        /// Зачем терпимость. Замер 07.09.2026: отказы приходили на страницы
        /// ПОИСКА (`tracker.php?nm=...`), тогда как страницы обхода той же
        /// cookie открывались, а браузер за те же пять минут нашёл ровно одну
        /// настоящую задачу против 47 «потерь». То есть трекер придирается к
        /// поиску, а мы из-за этого выбрасывали рабочую cookie у всех разом и
        /// роняли обход.
        /// </summary>
        public static bool ShouldDropClearance(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;

            var run = _mitigated.GetOrAdd(host, _ => new MitigationRun { Since = DateTime.UtcNow });

            lock (run)
            {
                var now = DateTime.UtcNow;

                if (now - run.Since > MitigationWindow)
                {
                    run.Since = now;
                    run.Count = 0;
                }

                run.Count++;

                if (run.Count < MitigationsToDrop)
                    return false;

                run.Since = now;
                run.Count = 0;
                return true;
            }
        }
        #endregion

        #region признак «нас снова просят решить задачу»
        /// <summary>
        /// Ответ означает, что cookie перестала работать.
        ///
        /// Отличать надо от обычного отказа страницы: 404 у снесённой темы —
        /// это свойство страницы, а не поломка cookie, и выбрасывать её
        /// незачем. Признаком считается только 403/503 вместе с разметкой
        /// задачи либо пустой ответ на месте страницы.
        /// </summary>
        public static bool ClearanceLost(int status, string body, bool cfMitigated = false)
        {
            // Заголовок `cf-mitigated` Cloudflare ставит именно тогда, когда
            // вмешалась сама. Это и есть настоящий признак.
            if (cfMitigated)
                return true;

            // Разметка задачи в теле — второй вид, старые проверки приходят
            // с кодом 200 и страницей «Just a moment…».
            if (CloudflareClearance.IsChallengeBody(body))
                return true;

            // Голый 403 отзывом НЕ считается.
            //
            // Так и вышла беда 07.09.2026: rutracker сам отвечает 403 на
            // разделы, куда нашей учётке нельзя, и обход натыкается на них
            // постоянно. Мы принимали это за отзыв cookie, выбрасывали
            // рабочую clearance и шли в браузер — 133 «потери» и 134 новых
            // cookie за пять минут, то есть петля. Обход в это время стоял.
            return false;
        }
        #endregion

        /// <summary>
        /// Забирает страницу быстрым путём. Возвращает код ответа и тело;
        /// код 0 означает, что не удалось достучаться до самой службы —
        /// тогда решает вызывающий, идти ли в браузер.
        /// </summary>
        public static async Task<(int status, string body, bool cfMitigated)> GetAsync(string url, Clearance clearance, string postData = null)
        {
            var conf = Conf;
            if (conf == null || clearance == null || string.IsNullOrWhiteSpace(url))
                return (0, null, false);

            var gate = Gate(conf);
            await gate.WaitAsync();
            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["url"] = url,
                    ["cookies"] = clearance.Cookies,
                    ["userAgent"] = clearance.UserAgent ?? string.Empty,
                    ["impersonate"] = conf.impersonate,
                    ["timeout"] = conf.timeoutSeconds
                };

                if (postData != null)
                    payload["postData"] = postData;

                // Хост, до которого нельзя ходить напрямую, быстрый путь берёт
                // через тот же выход, что и браузер: иначе он продолжает
                // стучаться с забаненного адреса и портить его репутацию.
                string через = ProxyForHost(url);
                if (через != null)
                    payload["proxy"] = через;

                using var client = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(conf.timeoutSeconds + 10)
                };

                using var content = new System.Net.Http.StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                using var response = await client.PostAsync(conf.url, content);

                var root = JObject.Parse(await response.Content.ReadAsStringAsync());

                int status = root.Value<int?>("status") ?? 0;
                string body = root.Value<string>("body");

                if (status == 0)
                {
                    string error = root.Value<string>("error");
                    if (!string.IsNullOrWhiteSpace(error))
                        JacBlackLog.Swallowed(JacBlackLogCategories.Host, $"cffetch: {url}: {error}", null);
                }

                bool mitigated = root.Value<bool?>("cfMitigated") ?? false;

                return (status, body, mitigated);
            }
            catch (Exception ex)
            {
                // Служба-помощник не ответила. Это не повод терять страницу:
                // наверху остаётся браузер, он медленный, но рабочий.
                JacBlackLog.Swallowed(JacBlackLogCategories.Host,
                    $"cffetch недоступен ({ex.GetType().Name}), уходим на браузер", ex);
                return (0, null, false);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Выход для этого адреса — из общих правил `globalproxy`. Правило там
        /// значит «напрямую туда нельзя», и это одинаково верно для обычного
        /// клиента, браузера и быстрого пути.
        /// </summary>
        static string ProxyForHost(string url)
        {
            var rules = AppInit.conf?.globalproxy;
            if (rules == null || string.IsNullOrWhiteSpace(url))
                return null;

            foreach (var rule in rules)
            {
                if (rule?.list == null || rule.list.Count == 0 || string.IsNullOrWhiteSpace(rule.pattern))
                    continue;

                try
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(url, rule.pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        continue;
                }
                catch (ArgumentException)
                {
                    continue;
                }

                foreach (string адрес in rule.list)
                {
                    if (!string.IsNullOrWhiteSpace(адрес))
                        return адрес.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// Своё ограничение одновременности. У быстрого пути нет естественного
        /// тормоза браузера, и без него обход упрётся в ограничение частоты
        /// самого трекера — то есть в 429 и бан, а не в скорость.
        /// </summary>
        static SemaphoreSlim Gate(Models.AppConf.CfFetchSettings conf)
        {
            int size = conf.maxConcurrent > 0 ? conf.maxConcurrent : 1;

            if (_gate == null || _gateSize != size)
            {
                _gate = new SemaphoreSlim(size, size);
                _gateSize = size;
            }

            return _gate;
        }
    }
}
