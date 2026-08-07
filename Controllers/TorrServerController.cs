using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace JacBlack.Controllers
{
    /// <summary>
    /// Прокси к пользовательскому TorrServer.
    ///
    /// Зачем сервер, а не браузер: jac.black открыт по HTTPS, а домашний/публичный
    /// TorrServer почти всегда по HTTP. Браузер блокирует запрос HTTPS→HTTP (mixed
    /// content) ещё до отправки — кнопка «В TorrServer» из веба не работала в принципе,
    /// хотя сам TorrServer жив. Здесь запрос уходит с сервера: mixed content и CORS
    /// исчезают. Учётки идут насквозь (в запросе), на сервере не хранятся.
    ///
    /// SSRF: адрес задаёт клиент, поэтому приватные и петлевые адреса запрещены —
    /// иначе публичный jac.black стал бы инструментом простукивания внутренней сети.
    /// </summary>
    [Route("/torrserver")]
    public class TorrServerController : Controller
    {
        public class AddRequest
        {
            public string baseUrl { get; set; }
            public string login { get; set; }
            public string password { get; set; }
            public string magnet { get; set; }
        }

        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        [HttpPost]
        [Route("add")]
        async public Task<IActionResult> Add([FromBody] AddRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.baseUrl))
                return Json(new { ok = false, code = "missingUrl" });

            if (string.IsNullOrWhiteSpace(req.magnet) ||
                !req.magnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                return Json(new { ok = false, code = "invalidMagnet" });

            if (!TryBuildTarget(req.baseUrl, out var origin, out var deny))
                return Json(new { ok = false, code = "request", message = deny });

            var torUrl = origin + "/torrents";
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(
                new { action = "add", link = req.magnet, save_to_db = true });

            var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            using var msg = new HttpRequestMessage(HttpMethod.Post, torUrl) { Content = content };
            if (!string.IsNullOrEmpty(req.login))
                msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{req.login}:{req.password}")));

            try
            {
                using var res = await http.SendAsync(msg);
                if (res.IsSuccessStatusCode)
                    return Json(new { ok = true });
                if ((int)res.StatusCode == 401)
                    return Json(new { ok = false, code = "unauthorized" });
                return Json(new { ok = false, code = "request", status = (int)res.StatusCode });
            }
            catch
            {
                return Json(new { ok = false, code = "request" });
            }
        }

        /// <summary>Проверка связи: GET /echo у TorrServer (версия). Для кнопки «Проверить».</summary>
        [HttpGet]
        [Route("check")]
        async public Task<IActionResult> Check(string baseUrl)
        {
            if (!TryBuildTarget(baseUrl, out var origin, out var deny))
                return Json(new { ok = false, code = "request", message = deny });
            try
            {
                using var res = await http.GetAsync(origin + "/echo");
                var text = res.IsSuccessStatusCode ? await res.Content.ReadAsStringAsync() : null;
                return Json(new { ok = res.IsSuccessStatusCode, version = text?.Trim(), status = (int)res.StatusCode });
            }
            catch
            {
                return Json(new { ok = false, code = "request" });
            }
        }

        /// <summary>Разбирает базовый адрес и запрещает приватные/петлевые цели (SSRF).</summary>
        static bool TryBuildTarget(string baseUrl, out string origin, out string deny)
        {
            origin = null; deny = null;
            if (string.IsNullOrWhiteSpace(baseUrl)) { deny = "no url"; return false; }
            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            { deny = "bad url"; return false; }

            var host = uri.Host;
            System.Net.IPAddress[] addrs;
            if (System.Net.IPAddress.TryParse(host, out var direct))
                addrs = new[] { direct };
            else
            {
                try { addrs = System.Net.Dns.GetHostAddresses(host); }
                catch { deny = "dns"; return false; }
            }
            if (addrs.Length == 0) { deny = "dns"; return false; }
            if (addrs.Any(IsPrivate)) { deny = "private address blocked"; return false; }

            origin = uri.GetLeftPart(UriPartial.Authority);
            return true;
        }

        static bool IsPrivate(System.Net.IPAddress ip)
        {
            if (System.Net.IPAddress.IsLoopback(ip)) return true;
            var b = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                if (b[0] == 10) return true;
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                if (b[0] == 192 && b[1] == 168) return true;
                if (b[0] == 169 && b[1] == 254) return true;   // link-local
                if (b[0] == 127) return true;
                if (b[0] == 0) return true;
                return false;
            }
            // IPv6: петлевой уже отсеян; блокируем ULA fc00::/7 и link-local fe80::/10
            if ((b[0] & 0xfe) == 0xfc) return true;
            if (b[0] == 0xfe && (b[1] & 0xc0) == 0x80) return true;
            return false;
        }
    }
}
