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

        /// <summary>
        /// Состояние одного браузера: очередь к нему, его сессия и сторож
        /// простоя. Браузеров теперь может быть два — общий и отдельный для
        /// обходов, — и путать их состояния нельзя.
        /// </summary>
        sealed class Lane
        {
            // Запросы к одному браузеру идут строго по очереди: параллельные
            // вызовы разгоняли нагрузку до 22 при двух ядрах, а отдачи не
            // прибавляли — замер 07.09.2026 показал, что несколько сессий
            // внутри одного экземпляра его не ускоряют.
            public readonly SemaphoreSlim Gate = new(1, 1);

            public bool SessionAlive;
            public DateTime LastUse = DateTime.MinValue;
            public Timer IdleTimer;
        }

        static readonly ConcurrentDictionary<string, Lane> _lanes =
            new(StringComparer.OrdinalIgnoreCase);

        static Lane LaneOf(string url) => _lanes.GetOrAdd(url ?? "", _ => new Lane());

        /// <summary>Этот запрос — часть обхода, значит идёт к своему браузеру.</summary>
        static readonly AsyncLocal<bool> _crawlLane = new();

        /// <summary>
        /// Пометить всё, что делается внутри, как обход. Ставится один раз —
        /// прослойкой по пути `/cron/` — и дальше сама течёт по вложенным
        /// вызовам.
        /// </summary>
        public static IDisposable UseCrawlLane()
        {
            _crawlLane.Value = true;
            return new LaneScope();
        }

        sealed class LaneScope : IDisposable
        {
            public void Dispose() => _crawlLane.Value = false;
        }

        /// <summary>
        /// Браузер сейчас занят: очередь к нему не пуста.
        ///
        /// Нужно тем, кто может обойтись без него. Живые сиды — украшение
        /// выдачи, а обход трекера — её содержание, и когда браузер один на
        /// всех, украшение должно уступать. Замер 07.09.2026: 134 из 204
        /// обращений к браузеру за пять минут были поиском живых сидов, и
        /// обход в это время стоял.
        /// </summary>
        public static bool BrowserBusy
        {
            get
            {
                var conf = Conf;
                return conf.Url != null && LaneOf(conf.Url).Gate.CurrentCount == 0;
            }
        }

        static FlareSolverrSettingsView Conf
        {
            get
            {
                var c = AppInit.conf?.flaresolverr;

                // Выключено или не настроено — отдаём пустую запись,
                // у неё Url равен null, и все пути наверху это проверяют.
                return c == null || !c.enable || string.IsNullOrWhiteSpace(c.url)
                    ? default
                    : new FlareSolverrSettingsView(c, _crawlLane.Value);
            }
        }

        readonly struct FlareSolverrSettingsView
        {
            public readonly string Url;
            public readonly int MaxTimeoutMs;
            public readonly int SessionIdleMinutes;
            public readonly int GuardedHours;
            public readonly int RecheckMinutes;

            public FlareSolverrSettingsView(Models.AppConf.FlareSolverrSettings c, bool crawl = false)
            {
                // У обхода свой браузер, если он задан: тогда его очередь и его
                // сессия отдельные, и поиск живых сидов ему не мешает.
                Url = crawl && !string.IsNullOrWhiteSpace(c.crawlUrl) ? c.crawlUrl : c.url;
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
        /// <param name="postData">
        /// Тело формы, если это POST; null означает обычный GET. Понадобилось
        /// 10.09.2026, когда kinozal ушёл под проверку: инфо-хеш там берётся
        /// POST-ом на get_srv_details.php, обход стоял только на GET, и новые
        /// раздачи молча перестали попадать в базу. Листинг при этом браузер
        /// получал исправно, обновления шли, и в журнале было ровно
        /// «добавлено=0 обновлено=1076» без единой ошибки.
        /// </param>
        public static async Task<string> FetchAsync(string url, string cookie = null, string postData = null)
        {
            var conf = Conf;
            if (conf.Url == null || string.IsNullOrWhiteSpace(url))
                return null;

            string host;
            try { host = new Uri(url).Host; }
            catch (UriFormatException) { return null; }

            // Сперва быстрый путь. Если браузер уже решил задачу для этого
            // хоста, страница берётся обычным клиентом с его отпечатком —
            // 0.13 с против 3.9 с. Браузер никуда не девается: сюда
            // возвращаемся, как только cookie перестала проходить.
            // Cookie отозвана — обновлять её идёт ОДИН, остальные ждут его и
            // возвращаются на быстрый путь. Без этого каждый отзыв стоил
            // лавины: замер 07.09.2026 — на отзыв в браузер ломились все
            // висящие запросы разом (137 обращений за десять минут вместо 26),
            // и обход в это время стоял, продвигаясь рывками: 102 страницы за
            // восемь минут, потом одна за девять.
            for (int round = 0; round < 3; round++)
            {
                var (fast, fastHtml) = await TryFastAsync(host, url, cookie, postData);

                if (fast == FastOutcome.Ok)
                    return fastHtml;

                // Сайт ответил по существу — например, тема снесена и это 404.
                // Идти за тем же ответом в браузер незачем: он ответит так же,
                // только в тридцать раз медленнее.
                if (fast == FastOutcome.PageFailed)
                    return null;

                // Быстрого пути для этой страницы нет — берём её браузером
                // сами, не трогая cookie: она жива и нужна остальным.
                if (fast == FastOutcome.NotAvailable)
                    break;

                // Cookie отозвана. Обновлять её идёт ОДИН: если право за нами,
                // сами и пойдём в браузер; иначе дождались чужого обновления
                // и пробуем быстрый путь заново.
                if (await ClearanceRenewedAsync(host))
                    break;
            }

            var lane = LaneOf(conf.Url);

            await lane.Gate.WaitAsync();
            try
            {
                if (!lane.SessionAlive && !await CreateSessionAsync(conf, lane))
                    return null;

                var (outcome, html) = await RequestAsync(conf, lane, url, cookie, postData);

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
                    await DestroySessionAsync(conf, lane);

                    if (!await CreateSessionAsync(conf, lane))
                        return null;

                    (outcome, html) = await RequestAsync(conf, lane, url, cookie, postData);

                    if (outcome == FetchOutcome.Ok)
                        JacBlackLog.Warning(JacBlackLogCategories.Host, $"{host}: получилось со второй попытки, сессия пересоздана");
                }

                // Сессия жива в любом случае, кроме уже обработанного выше:
                // отметку об использовании ставим и после неудачной страницы,
                // иначе полоса закрытых разделов усыпит браузер по простою.
                lane.LastUse = DateTime.UtcNow;
                ArmIdleTimer(conf, lane);

                return outcome == FetchOutcome.Ok ? html : null;
            }
            catch (Exception ex)
            {
                JacBlackLog.Error(JacBlackLogCategories.Host, $"FlareSolverr: {host}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            finally
            {
                // Право обновлять cookie отдаём здесь, а не раньше: пока мы
                // ходили в браузер, остальные ждали именно нас.
                ReleaseRenew(host);

                lane.Gate.Release();
            }
        }

        /// <summary>Кто сейчас обновляет cookie по этому хосту — по одному на хост.</summary>
        static readonly ConcurrentDictionary<string, SemaphoreSlim> _renewGates =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Сколько ждать чужого обновления cookie. Решение задачи занимает
        /// до полутора минут, поэтому ждём с запасом: простоять минуту и уйти
        /// быстрым путём дешевле, чем встать в очередь к браузеру самому.
        /// </summary>
        static readonly TimeSpan RenewWait = TimeSpan.FromSeconds(100);

        /// <summary>
        /// Возвращает true, если обновлять cookie предстоит НАМ, и false —
        /// если это уже делает кто-то другой и его результат дождались
        /// (либо не дождались, но ждать дольше бессмысленно).
        ///
        /// Право обновлять берётся без ожидания: кто первым добежал, тот и
        /// идёт в браузер. Остальные ждут появления свежей cookie и
        /// возвращаются на быстрый путь.
        /// </summary>
        static async Task<bool> ClearanceRenewedAsync(string host)
        {
            var gate = _renewGates.GetOrAdd(host, _ => new SemaphoreSlim(1, 1));

            if (await gate.WaitAsync(0))
            {
                // Право за нами. Отпускаем его сразу после того, как браузер
                // отработает: сам поход делает вызывающий, а не мы, поэтому
                // освобождение стоит на его finally — здесь только отметка.
                _renewing[host] = gate;
                return true;
            }

            // Ждём чужого результата: как только cookie появилась, уходим
            // быстрым путём.
            var deadline = DateTime.UtcNow + RenewWait;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);

                if (CfFetch.For(host) != null)
                    return false;
            }

            return false;
        }

        /// <summary>Захваченные права на обновление — чтобы вернуть их после похода в браузер.</summary>
        static readonly ConcurrentDictionary<string, SemaphoreSlim> _renewing =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Вернуть право обновлять cookie, если мы его брали.</summary>
        static void ReleaseRenew(string host)
        {
            if (_renewing.TryRemove(host, out var gate))
                gate.Release();
        }

        /// <summary>Чем кончилась попытка обойтись без браузера.</summary>
        enum FastOutcome
        {
            /// <summary>Быстрого пути нет: он выключен или cookie ещё не добыта.</summary>
            NotAvailable,

            /// <summary>Страница получена.</summary>
            Ok,

            /// <summary>Сайт ответил, но страницы не дал: 404 у снесённой темы и подобное.</summary>
            PageFailed,

            /// <summary>Cookie перестала проходить — нужен браузер, чтобы решить задачу заново.</summary>
            ClearanceLost
        }

        /// <summary>
        /// Пробует взять страницу без браузера — обычным клиентом, но с
        /// отпечатком Chrome и cookie, которую браузер добыл раньше.
        /// </summary>
        static async Task<(FastOutcome outcome, string html)> TryFastAsync(
            string host, string url, string cookie, string postData)
        {
            var clearance = CfFetch.For(host);
            if (clearance == null)
                return (FastOutcome.NotAvailable, null);

            // К cookie от браузера добавляем то, что просил вызывающий:
            // у трекеров с входом там своя сессия, и без неё страница
            // отдаётся гостевая.
            var merged = MergeCookies(clearance.Cookies, cookie);

            var (status, body, cfMitigated) = await CfFetch.GetAsync(url, new CfFetch.Clearance
            {
                Cookies = merged,
                UserAgent = clearance.UserAgent,
                At = clearance.At
            }, postData);

            // До самой службы-помощника не достучались — это не про cookie.
            if (status == 0)
                return (FastOutcome.NotAvailable, null);

            if (CfFetch.ClearanceLost(status, body, cfMitigated))
            {
                // Один отказ — ещё не приговор cookie: трекер придирается к
                // страницам поиска, а обход теми же ключами идёт. Выбрасываем
                // её только когда отказы пошли подряд.
                if (CfFetch.ShouldDropClearance(host))
                {
                    CfFetch.Forget(host);
                    return (FastOutcome.ClearanceLost, null);
                }

                // Эту страницу возьмём браузером, остальные пусть идут быстро.
                return (FastOutcome.NotAvailable, null);
            }

            if (status == 200 && !string.IsNullOrWhiteSpace(body))
                return (FastOutcome.Ok, body);

            return (FastOutcome.PageFailed, null);
        }

        /// <summary>
        /// Складывает два набора cookie в один. При совпадении имени
        /// побеждает тот, что просил вызывающий: его сессия свежее.
        /// </summary>
        static string MergeCookies(string fromBrowser, string fromCaller)
        {
            if (string.IsNullOrWhiteSpace(fromCaller))
                return fromBrowser;

            var jar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in new[] { fromBrowser, fromCaller })
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

        /// <summary>
        /// Забирает из ответа браузера cookie и то, чем он представлялся, —
        /// это и есть ключ к быстрому пути. Зовётся после каждого удачного
        /// обращения: после входа на трекер cookie меняются.
        /// </summary>
        static async Task RememberClearance(string url, JObject solution)
        {
            if (solution == null)
                return;

            string host;
            try { host = new Uri(url).Host; }
            catch (UriFormatException) { return; }

            // Уже есть рабочая cookie — не трогаем: замена стоила дорого.
            // Свежая от браузера может оказаться негодной, и тогда мы обменяли
            // бы рабочую на нерабочую.
            if (CfFetch.For(host) != null)
                return;

            var jar = solution["cookies"] as JArray;
            if (jar == null || jar.Count == 0)
                return;

            var sb = new StringBuilder();
            foreach (var c in jar)
            {
                string name = c.Value<string>("name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (sb.Length > 0)
                    sb.Append("; ");

                sb.Append(name).Append('=').Append(c.Value<string>("value"));
            }

            var candidate = new CfFetch.Clearance
            {
                Cookies = sb.ToString(),
                UserAgent = solution.Value<string>("userAgent"),
                At = DateTime.UtcNow
            };

            // Проверяем делом: «браузер прошёл» больше не значит «пройдём и мы».
            if (!await CfFetch.ValidateAsync(url, candidate))
            {
                CfFetch.BlockFastPath(host);
                return;
            }

            CfFetch.Remember(host, candidate.Cookies, candidate.UserAgent);
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

            var lane = LaneOf(conf.Url);

            await lane.Gate.WaitAsync();
            try
            {
                if (!lane.SessionAlive && !await CreateSessionAsync(conf, lane))
                    return null;

                var payload = new Dictionary<string, object>
                {
                    ["cmd"] = "request.post",
                    ["session"] = SessionName,
                    ["url"] = url,
                    ["postData"] = formData ?? string.Empty,
                    ["maxTimeout"] = conf.MaxTimeoutMs
                };

                var root = await CallAsync(conf, lane, payload, conf.MaxTimeoutMs + 30000);
                if (root == null || !string.Equals(root.Value<string>("status"), "ok", StringComparison.OrdinalIgnoreCase))
                    return null;

                lane.LastUse = DateTime.UtcNow;

                // Вход на трекер меняет cookie, и быстрый путь должен ходить
                // с новыми: со старыми поиск отдаёт гостевую страницу без
                // единой строки, а это выглядит как «трекер сломался».
                await RememberClearance(url, root.Value<JObject>("solution"));

                return root["solution"]?.Value<string>("response");
            }
            catch (Exception ex)
            {
                JacBlackLog.Error(JacBlackLogCategories.Host, $"FlareSolverr: отправка формы не удалась: {ex.GetType().Name}");
                return null;
            }
            finally
            {
                lane.Gate.Release();
            }
        }

        static async Task<(FetchOutcome outcome, string html)> RequestAsync(FlareSolverrSettingsView conf, Lane lane, string url, string cookie, string postData = null)
        {
            var payload = new Dictionary<string, object>
            {
                // Форма отправляется тем же путём и в той же сессии, что и
                // страницы: браузер уже прошёл проверку, а отдельная сессия
                // под POST стоила бы ещё одного решения задачи.
                ["cmd"] = postData == null ? "request.get" : "request.post",
                ["session"] = SessionName,
                ["url"] = url,
                ["maxTimeout"] = conf.MaxTimeoutMs
            };

            if (postData != null)
                payload["postData"] = postData;

            var jar = ParseCookies(cookie);
            if (jar.Count > 0)
                payload["cookies"] = jar;

            var root = await CallAsync(conf, lane, payload, conf.MaxTimeoutMs + 30000);

            // До службы не достучались — это про браузер, не про страницу.
            if (root == null)
                return (FetchOutcome.BrowserFailed, null);

            if (!string.Equals(root.Value<string>("status"), "ok", StringComparison.OrdinalIgnoreCase))
            {
                string message = root.Value<string>("message") ?? "";
                JacBlackLog.Error(JacBlackLogCategories.Host, $"FlareSolverr отказал: {message}");

                // Такое сообщение означает, что сессии больше нет.
                if (message.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0)
                    lane.SessionAlive = false;

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

            // Задача решена, cookie у нас — дальше по этому хосту браузер
            // не нужен, пока она не перестанет проходить.
            await RememberClearance(url, solution);

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
        static async Task<bool> CreateSessionAsync(FlareSolverrSettingsView conf, Lane lane)
        {
            var root = await CallAsync(conf, lane, new Dictionary<string, object>
            {
                ["cmd"] = "sessions.create",
                ["session"] = SessionName
            }, conf.MaxTimeoutMs + 30000);

            bool ok = root != null &&
                      string.Equals(root.Value<string>("status"), "ok", StringComparison.OrdinalIgnoreCase);

            lane.SessionAlive = ok;

            if (ok)
                JacBlackLog.Warning(JacBlackLogCategories.Host, "FlareSolverr: сессия браузера создана");
            else
                JacBlackLog.Error(JacBlackLogCategories.Host, $"FlareSolverr: сессию создать не удалось: {root?.Value<string>("message")}");

            return ok;
        }

        /// <summary>Закрывает сессию, не поднимая шума: она могла уже умереть сама.</summary>
        static async Task DestroySessionAsync(FlareSolverrSettingsView conf, Lane lane)
        {
            if (!lane.SessionAlive)
                return;

            await CallAsync(conf, lane, new Dictionary<string, object>
            {
                ["cmd"] = "sessions.destroy",
                ["session"] = SessionName
            }, 60000);

            lane.SessionAlive = false;
        }

        static void ArmIdleTimer(FlareSolverrSettingsView conf, Lane lane)
        {
            if (conf.SessionIdleMinutes <= 0)
                return;

            string url = conf.Url;
            lane.IdleTimer ??= new Timer(_ => CloseIfIdle(url, lane), null, Timeout.Infinite, Timeout.Infinite);

            var period = TimeSpan.FromMinutes(1);
            lane.IdleTimer.Change(period, period);
        }

        /// <summary>Закрывает простаивающую сессию: браузер держит около 700 МБ.</summary>
        static void CloseIfIdle(string url, Lane lane)
        {
            var c = AppInit.conf?.flaresolverr;
            if (c == null || url == null || !lane.SessionAlive || c.sessionIdleMinutes <= 0)
                return;

            // Сторож живёт на таймере, а не внутри запроса, поэтому пометки
            // полосы у него нет: адрес берём тот, с которым его завели.
            var conf = new FlareSolverrSettingsView(c, crawl: url == c.crawlUrl);

            if (DateTime.UtcNow < lane.LastUse.AddMinutes(conf.SessionIdleMinutes))
                return;

            // Если сейчас идёт запрос — не мешаем, закроем на следующем тике.
            if (!lane.Gate.Wait(0))
                return;

            try
            {
                CallAsync(conf, lane, new Dictionary<string, object>
                {
                    ["cmd"] = "sessions.destroy",
                    ["session"] = SessionName
                }, 60000).GetAwaiter().GetResult();

                lane.SessionAlive = false;
                lane.IdleTimer?.Change(Timeout.Infinite, Timeout.Infinite);

                JacBlackLog.Warning(JacBlackLogCategories.Host, "FlareSolverr: сессия закрыта по простою, память освобождена");
            }
            catch (Exception ex)
            {
                JacBlackLog.Error(JacBlackLogCategories.Host, $"FlareSolverr: не удалось закрыть сессию: {ex.Message}");
            }
            finally
            {
                lane.Gate.Release();
            }
        }
        #endregion

        static async Task<JObject> CallAsync(FlareSolverrSettingsView conf, Lane lane, Dictionary<string, object> payload, int timeoutMs)
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
                lane.SessionAlive = false;
                return null;
            }
        }
    }
}
