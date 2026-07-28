using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Logging;
using JacRed.Models;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Persistence
{
    public partial class FileDB
    {
        #region Cron
        static bool TryEvictCacheEntry(string key)
        {
            if (!openWriteTask.TryGetValue(key, out WriteTaskModel wtm) || wtm.openconnection > 0)
                return false;

            if (!openWriteTask.TryRemove(key, out wtm))
                return false;

            // Запись вытесняется из кеша, и это последний шанс сохранить накопленное.
            // Раньше сбой здесь проглатывался — накопленные торренты пропадали
            // бесследно, а обход рапортовал, что всё сохранено.
            try { wtm.db.SaveChangesIfNeeded(); }
            catch (Exception ex) { JacRedLog.Swallowed(JacRedLogCategories.Fdb, $"вытеснение {key}: сохранить не удалось", ex, LogLevel.Error); }

            return true;
        }

        async public static Task Cron(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);

                if (!AppInit.conf.evercache.enable || 0 >= AppInit.conf.evercache.validHour)
                    continue;

                try
                {
                    int evicted = openWriteTask.ToArray()
                        .Where(i => DateTime.UtcNow > i.Value.lastread.AddHours(AppInit.conf.evercache.validHour))
                        .Count(i => TryEvictCacheEntry(i.Key));
                    if (evicted > 0)
                        JacRedLog.Warning(JacRedLogCategories.Fdb, $"evicted {evicted} cache entries (validHour) / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                }
                catch (Exception ex) { JacRedLog.Swallowed(JacRedLogCategories.Fdb, "уборка кеша по времени", ex); }
            }
        }

        async public static Task CronFast(CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);

                if (!AppInit.conf.evercache.enable || 0 >= AppInit.conf.evercache.validHour)
                    continue;

                try
                {
                    if (openWriteTask.Count > AppInit.conf.evercache.maxOpenWriteTask)
                    {
                        var query = openWriteTask.Where(i => DateTime.Now > i.Value.create.AddMinutes(10));
                        query = query.OrderBy(i => i.Value.countread).ThenBy(i => i.Value.lastread);

                        int dropped = query.Take(AppInit.conf.evercache.dropCacheTake).Count(i => TryEvictCacheEntry(i.Key));
                        if (dropped > 0)
                            JacRedLog.Warning(JacRedLogCategories.Fdb, $"dropped {dropped} cache entries (maxOpenWriteTask) / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    }
                }
                catch (Exception ex) { JacRedLog.Swallowed(JacRedLogCategories.Fdb, "уборка кеша по размеру", ex); }
            }
        }
        #endregion
    }
}
