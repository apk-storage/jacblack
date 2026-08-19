using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Trackers.Nyaa;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacBlack.Controllers.Cron
{
    /// <summary>
    /// nyaa.si — источник аниме взамен AnimeTosho, чья лента встала 8 мая 2026.
    /// Config: init.yaml Nyaa (host, useproxy). Хеш приходит прямо в ленте,
    /// поэтому magnet собирается без единого дополнительного запроса.
    /// </summary>
    [Route("/cron/nyaa/[action]")]
    public class NyaaController : BaseController
    {
        readonly NyaaSyncService _syncService;

        public NyaaController(IMemoryCache memoryCache, NyaaSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>Обход ленты по разделам аниме. pages — сколько страниц брать в каждом.</summary>
        public Task<string> Parse(int pages = 1, CancellationToken cancellationToken = default)
        {
            return _syncService.ParseAsync(pages, cancellationToken);
        }

        /// <summary>
        /// Обход в глубину по HTML-листингу. Лента дальше сотой страницы не
        /// пускает и сортировку игнорирует, а листинг её понимает: `old=true`
        /// открывает другой конец выдачи — самые старые раздачи.
        /// </summary>
        public Task<string> Listing(int pages = 20, bool old = false, CancellationToken cancellationToken = default)
        {
            return _syncService.ParseListingAsync(pages, old, cancellationToken);
        }
    }
}
