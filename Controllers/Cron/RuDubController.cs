using System.Threading.Tasks;
using JacBlack.Infrastructure.Trackers.RuDub;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacBlack.Controllers.Cron
{
    [Route("/cron/rudub/[action]")]
    public class RuDubController : BaseController
    {
        readonly RuDubSyncService _syncService;

        public RuDubController(IMemoryCache memoryCache, RuDubSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        async public Task<string> Parse(int page)
        {
            return await _syncService.ParseAsync(page);
        }

        async public Task<string> ParseLatest(int pages = 5)
        {
            return await _syncService.ParseLatestAsync(pages);
        }
    }
}
