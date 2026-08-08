using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JacBlack.Controllers
{
    /// <summary>
    /// Прокси авторизации CUB для функции «В Лампе».
    ///
    /// Зачем сервер, а не браузер: вход в аккаунт CUB — это POST на
    /// `https://cub.rip/api/device/add`, а это чужой домен. Cross-origin POST из
    /// браузера cub.rip не разрешает (нет CORS-заголовков), и запрос падает ещё до
    /// ответа. Отсюда запрос уходит с нашего сервера — CORS исчезает. Ответ
    /// (объект аккаунта) отдаётся клиенту как есть; на сервере ничего не хранится.
    ///
    /// SSRF тут не про адрес (он фиксирован — cub.rip), а про то, чтобы не дать
    /// прокинуть произвольное тело: принимаем ровно код (число), больше ничего.
    /// </summary>
    [Route("/cub")]
    public class CubController : Controller
    {
        const string CubDeviceAdd = "https://cub.rip/api/device/add";

        // Отдельный клиент: у CUB бывает медленный ответ на добавление устройства.
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        public class DeviceAddRequest
        {
            public string code { get; set; }
        }

        [HttpPost]
        [Route("device-add")]
        public async Task<IActionResult> DeviceAdd([FromBody] DeviceAddRequest req)
        {
            string code = (req?.code ?? "").Trim();
            if (!Regex.IsMatch(code, "^[0-9]{1,12}$"))
                return StatusCode(400, new { ok = false, code = "invalidCode", message = "Код должен быть числом с cub.rip/add" });

            // CUB ждёт число, а не строку.
            string body = Newtonsoft.Json.JsonConvert.SerializeObject(new { code = long.Parse(code) });

            var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            try
            {
                using var msg = new HttpRequestMessage(HttpMethod.Post, CubDeviceAdd) { Content = content };
                // CUB смотрит на источник запроса — представляемся как обычный клиент.
                msg.Headers.TryAddWithoutValidation("Origin", "https://cub.rip");
                msg.Headers.TryAddWithoutValidation("Referer", "https://cub.rip/");

                using var resp = await http.SendAsync(msg);
                string text = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    // CUB на НЕВЕРНЫЙ код отвечает пятисоткой, а настоящую
                    // причину кладёт внутрь: {"error":true,"code":200,...}.
                    // Сама Лампа так его и читает: errorCode == 200 у неё
                    // означает «неверный код», а не сбой. Разводим их и мы,
                    // иначе просроченный код выглядит как поломка сервера.
                    if (CubErrorCode(text) == 200)
                        return StatusCode(400, new
                        {
                            ok = false,
                            code = "badCode",
                            message = "Код не подошёл. Возьмите свежий на cub.rip/add — он одноразовый и живёт недолго"
                        });

                    return StatusCode(502, new { ok = false, code = "cubError", status = (int)resp.StatusCode, message = CubMessage(text) });
                }

                // Ответ CUB — JSON аккаунта. Отдаём как есть, чтобы клиент положил
                // его в localStorage и использовал в сокете без изменений.
                return Content(text, "application/json");
            }
            catch (TaskCanceledException)
            {
                return StatusCode(504, new { ok = false, code = "timeout", message = "CUB не ответил вовремя" });
            }
            catch (Exception ex)
            {
                return StatusCode(502, new { ok = false, code = "request", message = ex.Message });
            }
        }

        /// <summary>
        /// Достаёт человеческую причину отказа из ответа CUB.
        ///
        /// Он отвечает объектом вида {"error":true,"code":200,"text":"Код не
        /// найден"}, и раньше эта строка уходила на экран целиком, вместе со
        /// скобками и служебными полями. Человек в диалоге видел бы JSON вместо
        /// подсказки, что код неверный или просрочен.
        ///
        /// Разобрать не удалось — отдаём как было: пустое сообщение хуже
        /// некрасивого.
        /// </summary>
        /// <summary>Внутренний код ошибки CUB, 0 — если его нет.</summary>
        static int CubErrorCode(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return 0;

            try
            {
                var o = Newtonsoft.Json.Linq.JObject.Parse(body);
                return (int?)o["code"] ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        static string CubMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "CUB отказал без объяснения";

            try
            {
                var o = Newtonsoft.Json.Linq.JObject.Parse(body);
                string text = (string)o["text"] ?? (string)o["message"];
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
            catch
            {
                // не JSON — ниже отдадим как есть
            }

            return body.Length > 200 ? body.Substring(0, 200) : body;
        }
    }
}
