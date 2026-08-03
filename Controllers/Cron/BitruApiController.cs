using System.Threading.Tasks;
using JacBlack.Infrastructure.Trackers.Bitru;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacBlack.Controllers.Cron
{
    /// <summary>
    /// Парсинг Bitru через официальный API (api.php?get=torrents).
    /// Лимит: макс. 5 запросов в сек на IP — между запросами задержка 250 ms.
    /// </summary>
    [Route("/cron/bitru/[action]")]
    public class BitruApiController : BaseController
    {
        readonly BitruApiSyncService _syncService;

        public BitruApiController(IMemoryCache memoryCache, BitruApiSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        async public Task<string> Parse(int limit = 100) =>
            await _syncService.ParseAsync(limit);

        async public Task<string> ParseFromDate(string lastnewtor, int limit = 100) =>
            await _syncService.ParseFromDateAsync(lastnewtor, limit);

        /// <summary>
        /// Полный обход архива: идёт по времени назад, пока раздачи не кончатся.
        /// Раньше его не было вовсе, и bitru подбирал только новинки — отсюда
        /// 48 844 записи против 147 543 у старой базы. Место остановки
        /// запоминается, поэтому прерванный обход продолжается, а не начинается
        /// заново.
        /// </summary>
        async public Task<string> ParseAllTask(int maxPages = 5000) =>
            await _syncService.ParseAllTaskAsync(maxPages);

        /// <summary>
        /// Обход архива по списку сайта, под входом. Через API архив не взять:
        /// он отдаёт только две страницы свежего, и это его устройство, а не
        /// ограничение для гостя. Место остановки запоминается.
        /// </summary>
        async public Task<string> ParseArchive(int maxPages = 3000) =>
            await _syncService.ParseArchiveAsync(maxPages);
    }
}
