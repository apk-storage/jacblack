using System;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Stats;
using JacBlack.Infrastructure.Logging;

namespace JacBlack.Infrastructure.Background
{
    public static class StatsCron
    {
        async public static Task Run(CancellationToken cancellationToken = default)
        {
            await Task.Delay(20_000, cancellationToken);

            try { StatsCollector.CollectAndWrite(); }
            catch (Exception ex) { JacBlackLog.Error(JacBlackLogCategories.Stats, $"startup collect error / {ex.Message}"); }

            while (!cancellationToken.IsCancellationRequested)
            {
                if (AppInit.conf?.timeStatsUpdate == -1)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                    continue;
                }

                var intervalMinutes = AppInit.conf?.timeStatsUpdate ?? 90;
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), cancellationToken);

                try { StatsCollector.CollectAndWrite(); }
                catch (Exception ex)
                {
                    JacBlackLog.Error(JacBlackLogCategories.Stats, $"error / {ex.Message}");
                    if (ex.StackTrace != null)
                        JacBlackLog.Debug(JacBlackLogCategories.Stats, ex.StackTrace);
                }
            }
        }
    }
}
