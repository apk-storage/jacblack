using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Trackers.Yts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacBlack.Controllers.Cron
{
    /// <summary>
    /// YTS — фильмы в сильном сжатии, около 76 тысяч, открытое API.
    ///
    /// Русской озвучки нет. Ценность двойная: маленькие файлы для слабого
    /// интернета и КОД IMDB вместе с названием и годом — из него собирается
    /// словарь для поиска по идентификатору.
    /// </summary>
    [Route("/cron/yts/[action]")]
    public class YtsController : BaseController
    {
        readonly YtsSyncService _syncService;

        public YtsController(IMemoryCache memoryCache, YtsSyncService syncService) : base(memoryCache)
        {
            _syncService = syncService;
        }

        /// <summary>Свежая часть каталога.</summary>
        public Task<string> Parse(int pages = 4, CancellationToken cancellationToken = default)
            => _syncService.ParseAsync(pages, cancellationToken);

        /// <summary>Глубокий обход каталога.</summary>
        public Task<string> ParseAllTask(int pages = 200, CancellationToken cancellationToken = default)
            => _syncService.ParseAllAsync(pages, cancellationToken);

        /// <summary>Точечный поиск, в том числе по коду IMDB.</summary>
        public Task<string> Search(string query, CancellationToken cancellationToken = default)
            => _syncService.SearchAsync(query, cancellationToken);
    }
}
