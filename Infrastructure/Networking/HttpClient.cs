using JacRed.Models.AppConf;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Networking
{
    public static class HttpClient
    {
        static string useragent => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36";

        #region webProxy
        static ConcurrentBag<string> proxyRandomList = new ConcurrentBag<string>();

        public static WebProxy webProxy()
        {
            if (proxyRandomList.Count == 0)
            {
                foreach (string ip in AppInit.conf.proxy.list.OrderBy(a => Guid.NewGuid()))
                    proxyRandomList.Add(ip);
            }

            proxyRandomList.TryTake(out string proxyip);

            ICredentials credentials = null;

            if (AppInit.conf.proxy.useAuth)
                credentials = new NetworkCredential(AppInit.conf.proxy.username, AppInit.conf.proxy.password);

            return new WebProxy(proxyip, AppInit.conf.proxy.BypassOnLocal, null, credentials);
        }

        static WebProxy CreateWebProxy(string proxyUrl, ProxySettings settings)
        {
            ICredentials credentials = null;

            if (settings != null && settings.useAuth)
                credentials = new NetworkCredential(settings.username, settings.password);

            return new WebProxy(proxyUrl, settings?.BypassOnLocal ?? false, null, credentials);
        }

        static List<WebProxy> ResolveProxies(string url, bool useproxy, WebProxy proxyOverride)
        {
            if (proxyOverride != null)
                return new List<WebProxy> { proxyOverride };

            if (AppInit.conf.globalproxy != null)
            {
                foreach (var p in AppInit.conf.globalproxy)
                {
                    if (p.list == null || p.list.Count == 0)
                        continue;

                    if (!Regex.IsMatch(url, p.pattern, RegexOptions.IgnoreCase))
                        continue;

                    return p.list
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(_ => Guid.NewGuid())
                        .Select(u => CreateWebProxy(u, p))
                        .ToList();
                }
            }

            if (AppInit.conf.proxy.list != null && AppInit.conf.proxy.list.Count > 0 && useproxy)
                return new List<WebProxy> { webProxy() };

            return new List<WebProxy>();
        }

        static HttpClientHandler CreateHandler(WebProxy proxy, DecompressionMethods decompression)
        {
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = decompression
            };

            handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

            if (proxy != null)
            {
                handler.UseProxy = true;
                handler.Proxy = proxy;
            }

            return handler;
        }
        #endregion

        #region Пул соединений
        /// <summary>
        /// Раньше на КАЖДЫЙ запрос создавался новый HttpClient со своим handler:
        /// это полное TCP- и TLS-рукопожатие на каждую страницу и накопление
        /// сокетов в TIME_WAIT. При обходе в 38 тысяч страниц — 38 тысяч
        /// рукопожатий. Клиенты кешируются по ключу «прокси + распаковка»,
        /// соединения переиспользуются.
        /// </summary>
        static readonly ConcurrentDictionary<string, System.Net.Http.HttpClient> _clients = new();

        static System.Net.Http.HttpClient SharedClient(WebProxy proxy, DecompressionMethods decompression)
        {
            string key = (proxy?.Address?.ToString() ?? "direct") + "|" + (int)decompression;

            return _clients.GetOrAdd(key, _ =>
            {
                var handler = new SocketsHttpHandler
                {
                    AutomaticDecompression = decompression,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = 8,
                    ConnectTimeout = TimeSpan.FromSeconds(15),
                    AllowAutoRedirect = true
                };

                // Проверка сертификатов включена. Раньше она была отключена
                // жёстко для всех запросов, включая входы на трекеры с логином
                // и паролем. Исключения перечислены в конфиге явно.
                handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None)
                        return true;

                    var tls = AppInit.conf?.tls;
                    if (tls == null || !tls.validate)
                        return true;

                    string host = (sender as System.Net.Security.SslStream)?.TargetHostName
                                  ?? (sender as HttpRequestMessage)?.RequestUri?.Host;

                    if (tls.IsAllowedInvalid(host))
                        return true;

                    JacRed.Infrastructure.Logging.JacRedLog.Warning(
                        JacRed.Infrastructure.Logging.JacRedLogCategories.Host,
                        $"сертификат не прошёл проверку: {host ?? "неизвестный хост"} — {errors}");

                    return false;
                };

                if (proxy != null)
                {
                    handler.UseProxy = true;
                    handler.Proxy = proxy;
                }

                // Таймаут задаётся на запрос через CancellationToken, а не на клиента:
                // клиент общий и живёт долго.
                return new System.Net.Http.HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            });
        }
        #endregion


        #region Get
        async public static ValueTask<string> Get(string url, Encoding encoding = default, string cookie = null, string referer = null, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, long MaxResponseContentBufferSize = 0, bool useproxy = false, WebProxy proxy = null, int httpversion = 1)
        {
            return (await BaseGetAsync(url, encoding, cookie: cookie, referer: referer, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, MaxResponseContentBufferSize: MaxResponseContentBufferSize, useproxy: useproxy, proxy: proxy, httpversion: httpversion)).content;
        }
        #endregion

        #region Get<T>
        async public static ValueTask<T> Get<T>(string url, Encoding encoding = default, string cookie = null, string referer = null, long MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, bool IgnoreDeserializeObject = false, bool useproxy = false, WebProxy proxy = null)
        {
            try
            {
                string html = (await BaseGetAsync(url, encoding, cookie: cookie, referer: referer, MaxResponseContentBufferSize: MaxResponseContentBufferSize, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, useproxy: useproxy, proxy: proxy)).content;
                if (html == null)
                    return default;

                if (IgnoreDeserializeObject)
                    return JsonConvert.DeserializeObject<T>(html, new JsonSerializerSettings { Error = (se, ev) => { ev.ErrorContext.Handled = true; } });

                return JsonConvert.DeserializeObject<T>(html);
            }
            catch
            {
                return default;
            }
        }
        #endregion

        #region BaseGetAsync
        async public static ValueTask<(string content, HttpResponseMessage response)> BaseGetAsync(string url, Encoding encoding = default, string cookie = null, string referer = null, int timeoutSeconds = 15, long MaxResponseContentBufferSize = 0, List<(string name, string val)> addHeaders = null, bool useproxy = false, WebProxy proxy = null, int httpversion = 1)
        {
            var proxies = ResolveProxies(url, useproxy, proxy);
            if (proxies.Count == 0)
                proxies.Add(null);

            string requestHost = null;
            // Битый адрес оставляет хост пустым, и это нормально: IsGuarded на null
            // ответит false, запрос пойдёт обычным путём и упадёт уже со своей ошибкой.
            try { requestHost = new Uri(url).Host; } catch (UriFormatException) { }

            // Про хост уже известно, что обычный клиент туда не пройдёт:
            // не тратим запрос на заведомый 403, сразу идём браузером.
            if (CloudflareClearance.IsGuarded(requestHost))
            {
                string viaBrowser = await CloudflareClearance.FetchAsync(url, cookie);
                if (!string.IsNullOrWhiteSpace(viaBrowser))
                    return (viaBrowser, OkResponse(url));
            }

            foreach (var px in proxies)
            {
                // Здесь был цикл на две попытки: замысел был переспросить с
                // cookie, полученной после вызова Cloudflare. Он давно не работал —
                // все ветки внутри либо возвращают, либо выходят к следующему
                // прокси, так что второй заход не случался ни разу, и компилятор
                // честно сообщал о недостижимом коде.
                //
                // Переспрашивать больше и незачем: браузер отдаёт саму страницу,
                // а не cookie для повторного захода.
                try
                {
                    {
                        var client = SharedClient(px, DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli);

                        // Заголовки задаются на запрос, а не на общего клиента:
                        // клиент переиспользуется разными трекерами с разными cookie.
                        var req = new HttpRequestMessage(HttpMethod.Get, url)
                        {
                            // Было HTTP/1.0, где keep-alive выключен по умолчанию —
                            // соединение закрывалось после каждого ответа.
                            Version = httpversion >= 2 ? new Version(2, 0) : new Version(1, 1),
                            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                        };

                        req.Headers.TryAddWithoutValidation("user-agent", useragent);

                        if (cookie != null)
                            req.Headers.TryAddWithoutValidation("cookie", cookie);

                        if (referer != null)
                            req.Headers.TryAddWithoutValidation("referer", referer);

                        if (addHeaders != null)
                        {
                            foreach (var item in addHeaders)
                                req.Headers.TryAddWithoutValidation(item.name, item.val);
                        }

                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

                        // Хост уже просил подождать — не ломимся. Ждём в пределах
                        // отпущенного нам времени; дольше — честнее пропустить
                        // страницу, чем висеть до собственного таймаута.
                        if (!await HostThrottle.WaitAsync(requestHost, TimeSpan.FromSeconds(timeoutSeconds)))
                            break;

                        using (HttpResponseMessage response = await client.SendAsync(req, timeoutCts.Token))
                        {
                            // Просьба сбавить обороты — это не отказ и не проверка
                            // Cloudflare. Запоминаем паузу по этому хосту; на
                            // остальные трекеры она не распространяется.
                            if (HostThrottle.IsThrottleResponse(response))
                            {
                                HostThrottle.Throttled(requestHost, response);
                                break;
                            }

                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                HostThrottle.Ok(requestHost);
                                // Проверку могли снять. Раз обычный клиент прошёл —
                                // возвращаемся к дешёвому пути, не дожидаясь, пока
                                // истекут guardedHours.
                                CloudflareClearance.Unguard(requestHost);
                            }

                            if (response.StatusCode != HttpStatusCode.OK)
                            {
                                // Прилетел вызов Cloudflare: запоминаем хост и
                                // забираем страницу браузером. Свой таймаут у
                                // браузера, поэтому token вызывающего сюда не идёт —
                                // с ним первое обращение всегда обрывалось бы.
                                //
                                // Одного заголовка мало: старые виды проверки
                                // приходят без `cf-mitigated`, разметкой в теле.
                                // А судить по `cf-ray`, как делалось раньше,
                                // нельзя вовсе — он стоит на каждом ответе любого
                                // сайта за Cloudflare, и обычная перегрузка
                                // трекера выглядела как проверка.
                                bool challenge = CloudflareClearance.IsChallenge(response);

                                if (!challenge
                                    && (response.StatusCode == HttpStatusCode.Forbidden
                                        || response.StatusCode == HttpStatusCode.ServiceUnavailable))
                                {
                                    try
                                    {
                                        challenge = CloudflareClearance.IsChallengeBody(
                                            await response.Content.ReadAsStringAsync());
                                    }
                                    catch
                                    {
                                        // Тело не прочиталось — остаёмся при «проверки
                                        // нет». Ошибочная пометка дороже: она уводит
                                        // хост в браузер на часы, и обход встаёт.
                                    }
                                }

                                if (challenge)
                                {
                                    CloudflareClearance.MarkGuarded(requestHost);

                                    string viaBrowser = await CloudflareClearance.FetchAsync(url, cookie);
                                    if (!string.IsNullOrWhiteSpace(viaBrowser))
                                        return (viaBrowser, OkResponse(url));
                                }

                                break;              // к следующему прокси
                            }

                            using (HttpContent content = response.Content)
                            {
                                if (encoding != default)
                                {
                                    string res = encoding.GetString(await content.ReadAsByteArrayAsync());
                                    if (string.IsNullOrWhiteSpace(res))
                                        break;

                                    Parsing.PageBlockDetector.ReportIfBlocked(requestHost, url, res);
                                    return (res, response);
                                }
                                else
                                {
                                    string res = await content.ReadAsStringAsync();
                                    if (string.IsNullOrWhiteSpace(res))
                                        break;

                                    Parsing.PageBlockDetector.ReportIfBlocked(requestHost, url, res);
                                    return (res, response);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Этот выход пробовать больше нечем — идём к следующему.
                    // Раньше здесь стоял break из цикла попыток, что означало
                    // ровно то же самое, только неочевидно.
                    Logging.JacRedLog.Swallowed(Logging.JacRedLogCategories.Host,
                        $"запрос не прошёл через {px?.Address?.Host ?? "прямое соединение"}: {url}",
                        ex, Microsoft.Extensions.Logging.LogLevel.Debug);
                }
            }

            return (null, new HttpResponseMessage()
            {
                StatusCode = HttpStatusCode.InternalServerError,
                RequestMessage = new HttpRequestMessage()
            });
        }

        /// <summary>Ответ-заглушка для страниц, добытых браузером: разборщики смотрят на код.</summary>
        static HttpResponseMessage OkResponse(string url)
        {
            var request = new HttpRequestMessage();
            // Адрес тут только для сведения: разборщики смотрят на код ответа.
            // Битый адрес оставит заглушку без него, и это ничего не меняет.
            try { request.RequestUri = new Uri(url); } catch (UriFormatException) { }

            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        }
        #endregion


        #region Post
        public static ValueTask<string> Post(string url, string data, string cookie = null, int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, bool useproxy = false, WebProxy proxy = null)
        {
            return Post(url, new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"), cookie: cookie, MaxResponseContentBufferSize: MaxResponseContentBufferSize, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, useproxy: useproxy, proxy: proxy);
        }

        async public static ValueTask<string> Post(string url, HttpContent data, Encoding encoding = default, string cookie = null, int MaxResponseContentBufferSize = 0, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, bool useproxy = false, WebProxy proxy = null)
        {
            var proxies = ResolveProxies(url, useproxy, proxy);
            if (proxies.Count == 0)
                proxies.Add(null);

            foreach (var px in proxies)
            {
                try
                {
                    {
                        var client = SharedClient(px, DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate);

                        var req = new HttpRequestMessage(HttpMethod.Post, url)
                        {
                            Content = data,
                            Version = new Version(1, 1),
                            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                        };

                        req.Headers.TryAddWithoutValidation("user-agent", useragent);
                        if (cookie != null)
                            req.Headers.TryAddWithoutValidation("cookie", cookie);

                        if (addHeaders != null)
                        {
                            foreach (var item in addHeaders)
                                req.Headers.TryAddWithoutValidation(item.name, item.val);
                        }

                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

                        using (HttpResponseMessage response = await client.SendAsync(req, timeoutCts.Token))
                        {
                            if (response.StatusCode != HttpStatusCode.OK)
                                continue;

                            using (HttpContent content = response.Content)
                            {
                                if (encoding != default)
                                {
                                    string res = encoding.GetString(await content.ReadAsByteArrayAsync());
                                    if (string.IsNullOrWhiteSpace(res))
                                        continue;

                                    return res;
                                }
                                else
                                {
                                    string res = await content.ReadAsStringAsync();
                                    if (string.IsNullOrWhiteSpace(res))
                                        continue;

                                    return res;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }
        #endregion

        #region Post<T>
        async public static ValueTask<T> Post<T>(string url, string data, string cookie = null, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, bool useproxy = false, Encoding encoding = default, WebProxy proxy = null, bool IgnoreDeserializeObject = false)
        {
            return await Post<T>(url, new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded"), cookie: cookie, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, useproxy: useproxy, encoding: encoding, proxy: proxy, IgnoreDeserializeObject: IgnoreDeserializeObject);
        }

        async public static ValueTask<T> Post<T>(string url, HttpContent data, string cookie = null, int timeoutSeconds = 15, List<(string name, string val)> addHeaders = null, bool useproxy = false, Encoding encoding = default, WebProxy proxy = null, bool IgnoreDeserializeObject = false)
        {
            try
            {
                string json = await Post(url, data, cookie: cookie, timeoutSeconds: timeoutSeconds, addHeaders: addHeaders, useproxy: useproxy, encoding: encoding, proxy: proxy);
                if (json == null)
                    return default;

                if (IgnoreDeserializeObject)
                    return JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings { Error = (se, ev) => { ev.ErrorContext.Handled = true; } });

                return JsonConvert.DeserializeObject<T>(json);
            }
            catch
            {
                return default;
            }
        }
        #endregion


        #region Download
        async public static ValueTask<byte[]> Download(string url, string cookie = null, string referer = null, int timeoutSeconds = 30, long MaxResponseContentBufferSize = 0, List<(string name, string val)> addHeaders = null, bool useproxy = false, WebProxy proxy = null)
        {
            var proxies = ResolveProxies(url, useproxy, proxy);
            if (proxies.Count == 0)
                proxies.Add(null);

            foreach (var px in proxies)
            {
                try
                {
                    {
                        var client = SharedClient(px, DecompressionMethods.Brotli | DecompressionMethods.GZip | DecompressionMethods.Deflate);

                        var req = new HttpRequestMessage(HttpMethod.Get, url)
                        {
                            Version = new Version(1, 1),
                            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                        };

                        req.Headers.TryAddWithoutValidation("user-agent", useragent);

                        if (cookie != null)
                            req.Headers.TryAddWithoutValidation("cookie", cookie);

                        if (referer != null)
                            req.Headers.TryAddWithoutValidation("referer", referer);

                        if (addHeaders != null)
                        {
                            foreach (var item in addHeaders)
                                req.Headers.TryAddWithoutValidation(item.name, item.val);
                        }

                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

                        using (HttpResponseMessage response = await client.SendAsync(req, timeoutCts.Token))
                        {
                            if (response.StatusCode != HttpStatusCode.OK)
                                continue;

                            using (HttpContent content = response.Content)
                            {
                                byte[] res = await content.ReadAsByteArrayAsync();
                                if (res.Length == 0)
                                    continue;

                                return res;
                            }
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }
        #endregion
    }
}
