using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Application.Index;
using JacBlack.Infrastructure.Tracks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacBlack.Infrastructure.Background
{
    public class FastDbRefreshWorker : BackgroundService
    {
        readonly IFastDbIndex _fastDbIndex;
        readonly ILogger<FastDbRefreshWorker> _logger;

        public FastDbRefreshWorker(IFastDbIndex fastDbIndex, ILogger<FastDbRefreshWorker> logger)
        {
            _fastDbIndex = fastDbIndex;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("fastdb worker started");

            // Словарь кодов IMDB нужен поиску по идентификатору. Файл маленький,
            // грузится мгновенно, поэтому делаем это до всего остального.
            try { Infrastructure.Persistence.ImdbIndex.Load(); }
            catch (Exception ex) { _logger.LogWarning(ex, "imdb index load"); }

            // Словарь кодов Кинопоиска — то же самое, но для русского кино,
            // где кода IMDB нет ни у кого.
            try { Infrastructure.Persistence.KinopoiskIndex.Load(); }
            catch (Exception ex) { _logger.LogWarning(ex, "kinopoisk index load"); }

            try { Infrastructure.Indexers.TrackerFreshness.Load(); }
            catch (Exception ex) { _logger.LogWarning(ex, "tracker freshness load"); }

            try { TracksDB.StartupInit(); }
            catch (IOException ex) { _logger.LogWarning(ex, "tracks startup"); }
            catch (UnauthorizedAccessException ex) { _logger.LogWarning(ex, "tracks startup"); }

            try { _fastDbIndex.Rebuild(); }
            catch (Exception ex) { _logger.LogError(ex, "fastdb startup rebuild"); }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try { _fastDbIndex.Rebuild(); }
                catch (Exception ex) { _logger.LogError(ex, "fastdb periodic rebuild"); }
            }
        }
    }
}
