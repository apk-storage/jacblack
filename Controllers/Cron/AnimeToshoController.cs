using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.AnimeTosho;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/animetosho/[action]")]
    public class AnimeToshoController : BaseController
    {
        readonly AnimeToshoSyncService _syncService;

        public AnimeToshoController(IMemoryCache memoryCache, AnimeToshoSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>
        /// Обход ленты. Без параметров берёт первую страницу;
        /// глубокий проход — parse?parseFrom=1&amp;parseTo=15, как у остальных аниме-трекеров.
        /// </summary>
        public Task<string> Parse(int parseFrom = 0, int parseTo = 0, CancellationToken cancellationToken = default)
        {
            return _syncService.ParseAsync(parseFrom, parseTo, cancellationToken);
        }

        /// <summary>Дозабор раздач одного тайтла по его идентификатору в AniDB.</summary>
        public Task<string> ParseByAnidb(long aid, CancellationToken cancellationToken = default)
        {
            return _syncService.ParseByAnidbAsync(aid, cancellationToken);
        }
    }
}
