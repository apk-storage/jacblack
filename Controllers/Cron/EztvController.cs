using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.Eztv;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    /// <summary>
    /// EZTV — англоязычные сериалы, около миллиона раздач, открытое API.
    ///
    /// Русской озвучки там нет, и заводится источник не ради неё: вместе с
    /// раздачей приходит КОД IMDB, а из него собирается словарь «код → название»
    /// для поиска по идентификатору без чужого сервиса.
    /// </summary>
    [Route("/cron/eztv/[action]")]
    public class EztvController : BaseController
    {
        readonly EztvSyncService _syncService;

        public EztvController(IMemoryCache memoryCache, EztvSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>Свежая часть ленты.</summary>
        public Task<string> Parse(int pages = 3, CancellationToken cancellationToken = default)
            => _syncService.ParseAsync(pages, cancellationToken);

        /// <summary>Глубокий обход — уходит дальше в историю.</summary>
        public Task<string> ParseAllTask(int pages = 100, CancellationToken cancellationToken = default)
            => _syncService.ParseAllAsync(pages, cancellationToken);
    }
}
