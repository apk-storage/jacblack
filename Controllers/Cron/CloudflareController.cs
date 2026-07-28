using System.Threading.Tasks;
using JacRed.Infrastructure.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    /// <summary>
    /// Прогрев проверки Cloudflare.
    ///
    /// Решение задачи занимает полторы минуты и требует ядра: замер 28.07.2026
    /// на awg показал 88 с на спокойной машине и отказ по таймауту, когда рядом
    /// идёт обход — браузер и обход дерутся за два ядра.
    ///
    /// Поэтому задача решается ЗАРАНЕЕ, отдельным заданием крона за несколько
    /// минут до обхода. Дальше страницы в той же сессии идут по 2-3 секунды
    /// и ядра почти не просят.
    /// </summary>
    [Route("/cron/cloudflare/[action]")]
    public class CloudflareController : BaseController
    {
        public CloudflareController(IMemoryCache memoryCache) : base(memoryCache)
        {
        }

        /// <summary>
        /// Открывает браузером указанный адрес, чтобы сессия была готова
        /// к приходу обхода. По умолчанию — rutracker, ради которого всё и затевалось.
        /// </summary>
        async public Task<IActionResult> Warmup(string url = "https://rutracker.org/forum/index.php")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            string host = null;
            try { host = new System.Uri(url).Host; } catch (System.UriFormatException) { }

            // Даже если хост ещё не помечен закрытым, помечаем: прогрев вызывают
            // осознанно, и следующий запрос должен идти сразу браузером.
            CloudflareClearance.MarkGuarded(host);

            string html = await CloudflareClearance.FetchAsync(url);

            return Json(new
            {
                ok = !string.IsNullOrWhiteSpace(html),
                host,
                length = html?.Length ?? 0,
                tookSeconds = System.Math.Round(sw.Elapsed.TotalSeconds, 1)
            });
        }
    }
}
