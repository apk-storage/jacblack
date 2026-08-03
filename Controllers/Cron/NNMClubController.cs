using System.Threading.Tasks;
using JacBlack.Infrastructure.Trackers.NNMClub;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacBlack.Controllers.Cron
{
    [Route("/cron/nnmclub/[action]")]
    public class NNMClubController : BaseController
    {
        readonly NNMClubSyncService _syncService;

        public NNMClubController(IMemoryCache memoryCache, NNMClubSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        async public Task<string> Parse(int page)
        {
            return await _syncService.ParseAsync(page);
        }

        async public Task<string> UpdateTasksParse()
        {
            return await _syncService.UpdateTasksParseAsync();
        }

        async public Task<string> ParseAllTask()
        {
            return await _syncService.ParseAllTaskAsync();
        }

        /// <summary>
        /// Обход архива по форумам через tracker.php. Портал для этого не
        /// годится: у трекера потолок в 200 результатов на любой запрос, и
        /// вглубь он просто не пускает. Место остановки запоминается.
        /// </summary>
        async public Task<string> ParseArchive(int maxForums = 1000) =>
            await _syncService.ParseArchiveAsync(maxForums);

        async public Task<string> ParseLatest(int pages = 5)
        {
            return await _syncService.ParseLatestAsync(pages);
        }
    }
}
