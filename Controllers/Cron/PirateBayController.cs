using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Trackers.PirateBay;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    /// <summary>
    /// The Pirate Bay через открытое API apibay.org — «массовый» источник.
    /// Русской озвучки там почти нет, но сиды на два порядка выше наших:
    /// в HD-разделах медиана больше тысячи. В Лампе есть фильтр по трекерам.
    /// </summary>
    [Route("/cron/piratebay/[action]")]
    public class PirateBayController : BaseController
    {
        readonly PirateBaySyncService _syncService;

        public PirateBayController(IMemoryCache memoryCache, PirateBaySyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>Сотня самых раздаваемых по каждому разделу видео.</summary>
        public Task<string> Parse(CancellationToken cancellationToken = default)
            => _syncService.ParseAsync(cancellationToken);

        /// <summary>
        /// Глубокий обход: спрашиваем TPB о названиях из своего словаря кодов.
        ///
        /// Обычный проход упирается в потолок 700 раздач — списки «сотня самых
        /// раздаваемых» по семи разделам, и больше их взяться неоткуда.
        /// Постраничности у API нет. Поэтому идём поиском по названиям,
        /// которых у нас 75 тысяч.
        /// </summary>
        public Task<string> ParseAllTask(int maxQueries = 20000, CancellationToken cancellationToken = default)
            => _syncService.ParseAllTaskAsync(maxQueries, cancellationToken);

        /// <summary>Точечный дозабор по названию.</summary>
        public Task<string> Search(string query, CancellationToken cancellationToken = default)
            => _syncService.SearchAsync(query, cancellationToken);
    }
}
