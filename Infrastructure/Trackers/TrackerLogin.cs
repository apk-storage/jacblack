using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Parsing;

namespace JacBlack.Infrastructure.Trackers
{
    /// <summary>
    /// Вход на трекер с формой и сохранение сессионной cookie.
    ///
    /// Вынесено в общее место, потому что устроено у всех одинаково: POST на
    /// takelogin.php, в ответ ставятся cookie, дальше они прикладываются к
    /// каждому запросу. Различаются только имена полей формы — у kinozal это
    /// username, у bitru login.
    ///
    /// Зачем это вообще нужно: гостю трекеры отдают лишь свежее. Замер по
    /// bitru 31.07.2026 — browse.php гостю отвечает 403 при любой странице,
    /// а под входом отдаёт 67 раздач и листается вглубь как минимум до
    /// двухтысячной страницы. То есть архив открывается только учётной записью.
    /// </summary>
    public static class TrackerLogin
    {
        /// <summary>
        /// Отправляет форму и возвращает готовый заголовок Cookie либо null.
        /// Сами значения нигде не логируются: в журнал идёт только факт входа —
        /// иначе логин и пароль сессии легли бы в файл открытым текстом.
        /// </summary>
        public static async Task<string> TakeLoginAsync(
            string trackerName,
            string host,
            string path,
            IDictionary<string, string> form,
            int timeoutSeconds = 15)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                ParserLog.Write(trackerName, "вход не выполнен: не задан адрес трекера");
                return null;
            }

            host = host.TrimEnd('/');

            try
            {
                var jar = new CookieContainer();
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = true,
                    CookieContainer = jar
                };
                handler.ServerCertificateCustomValidationCallback += (_, _, _, _) => true;

                using var client = new HttpClient(handler);
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("origin", host);
                client.DefaultRequestHeaders.Add("referer", $"{host}/");
                client.DefaultRequestHeaders.Add("upgrade-insecure-requests", "1");

                using var content = new FormUrlEncodedContent(form);
                using var response = await client.PostAsync($"{host}/{path.TrimStart('/')}", content);

                string cookie = FromContainer(jar, new Uri(host + "/")) ?? FromResponse(response);

                if (string.IsNullOrWhiteSpace(cookie))
                {
                    ParserLog.Write(trackerName, $"вход не выполнен: сессионных cookie в ответе нет, код {(int)response.StatusCode}");
                    return null;
                }

                ParserLog.Write(trackerName, "вход выполнен");
                return cookie;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or CookieException)
            {
                ParserLog.Write(trackerName, $"вход не выполнен: {ex.GetType().Name}");
                return null;
            }
        }

        static string FromContainer(CookieContainer jar, Uri host)
        {
            var cookies = jar.GetCookies(host).Cast<Cookie>().Where(c => !string.IsNullOrEmpty(c.Value)).ToList();
            return cookies.Count == 0 ? null : string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
        }

        /// <summary>
        /// Запасной разбор: некоторые трекеры ставят cookie так, что контейнер
        /// их не подхватывает — например с чужим доменом в атрибуте.
        /// </summary>
        static string FromResponse(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("set-cookie", out var headers))
                return null;

            var parts = new List<string>();
            foreach (string line in headers)
            {
                string pair = line.Split(';')[0].Trim();
                int eq = pair.IndexOf('=');
                if (eq > 0 && eq < pair.Length - 1)
                    parts.Add(pair);
            }

            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
    }
}
