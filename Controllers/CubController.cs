using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
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
    /// SSRF тут не про адрес (он выбирается из зашитого списка), а про то, чтобы
    /// не дать прокинуть произвольное тело: принимаем ровно код (число).
    ///
    /// Зеркала. `cub.rip` умер к 20.08.2026 — вход отваливался с «Failed to
    /// fetch», потому что домен был прибит одной константой. У Лампы зеркал
    /// много и они меняются, поэтому здесь список: идём по нему, пока кто-то не
    /// ответит, и запоминаем сработавшее до перезапуска.
    ///
    /// В список попадают ТОЛЬКО свои зеркала. `cub.tv` отвечает 200 и выглядит
    /// живым, но принадлежит не нам — отправлять туда код от учётки нельзя,
    /// поэтому его здесь нет и быть не должно. `cub.best` наш, поднимается
    /// 20.08.2026; пока не отвечает — перебор просто пройдёт мимо.
    /// </summary>
    [Route("/cub")]
    public class CubController : Controller
    {
        static readonly string[] CubMirrors =
        {
            "cub.red", "cub.black", "cub.best", "cub.rip", "cub.watch"
        };

        // Зеркало, ответившее последним. Начинаем со следующего раза с него —
        // перебор мёртвых доменов стоит человеку секунд ожидания.
        static volatile string _lastGood;

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

            string последняяОшибка = null;

            foreach (string зеркало in Порядок())
            {
                try
                {
                    // Тело одноразовое: HttpContent нельзя переиспользовать между
                    // попытками, иначе вторая уйдёт с пустым потоком.
                    var телоЗапроса = new StringContent(body, Encoding.UTF8);
                    телоЗапроса.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                    using var msg = new HttpRequestMessage(HttpMethod.Post, $"https://{зеркало}/api/device/add") { Content = телоЗапроса };
                    // CUB смотрит на источник запроса — представляемся как обычный клиент.
                    msg.Headers.TryAddWithoutValidation("Origin", $"https://{зеркало}");
                    msg.Headers.TryAddWithoutValidation("Referer", $"https://{зеркало}/");

                    using var resp = await http.SendAsync(msg);
                    string text = await resp.Content.ReadAsStringAsync();

                    if (!resp.IsSuccessStatusCode)
                    {
                        // CUB на НЕВЕРНЫЙ код отвечает пятисоткой, а настоящую
                        // причину кладёт внутрь: {"error":true,"code":200,...}.
                        // Сама Лампа так его и читает: errorCode == 200 у неё
                        // означает «неверный код», а не сбой. Разводим их и мы,
                        // иначе просроченный код выглядит как поломка сервера.
                        //
                        // Это ОТВЕТ зеркала, а не его молчание: перебирать
                        // дальше незачем — на соседнем код будет так же неверен.
                        if (CubErrorCode(text) == 200)
                        {
                            _lastGood = зеркало;
                            return StatusCode(400, new
                            {
                                ok = false,
                                code = "badCode",
                                message = $"Код не подошёл. Возьмите свежий на {зеркало}/add — он одноразовый и живёт недолго"
                            });
                        }

                        последняяОшибка = CubMessage(text);
                        continue;                       // зеркало отвечает, но плохо — пробуем следующее
                    }

                    _lastGood = зеркало;

                    // Ответ CUB — JSON аккаунта. Отдаём как есть, чтобы клиент положил
                    // его в localStorage и использовал в сокете без изменений.
                    return Content(text, "application/json");
                }
                catch (TaskCanceledException)
                {
                    последняяОшибка = $"{зеркало} не ответил вовремя";
                }
                catch (HttpRequestException ex)
                {
                    последняяОшибка = $"{зеркало}: {ex.Message}";
                }
            }

            return StatusCode(502, new
            {
                ok = false,
                code = "cubUnavailable",
                message = "Ни одно зеркало Лампы не ответило. " + (последняяОшибка ?? "")
            });

            IEnumerable<string> Порядок()
            {
                string первое = _lastGood;
                if (!string.IsNullOrEmpty(первое))
                    yield return первое;

                foreach (string m in CubMirrors)
                {
                    if (!string.Equals(m, первое, StringComparison.OrdinalIgnoreCase))
                        yield return m;
                }
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
