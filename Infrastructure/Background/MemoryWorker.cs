using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JacBlack.Infrastructure.Background
{
    public class MemoryWorker : BackgroundService
    {
        readonly ILogger<MemoryWorker> _logger;

        public MemoryWorker(ILogger<MemoryWorker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("memory worker started");
            try
            {
                await MemoryCron.Run(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "memory worker terminated unexpectedly");
            }
        }
    }
}
