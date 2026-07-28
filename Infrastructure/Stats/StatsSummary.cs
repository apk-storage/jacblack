using System;
using System.IO;
using System.Linq;
using JacRed.Infrastructure.Logging;
using Newtonsoft.Json.Linq;

namespace JacRed.Infrastructure.Stats
{
    /// <summary>Read-only access to Data/temp/stats.json (per-tracker counters).</summary>
    public static class StatsSummary
    {
        public static string ReadAllJson()
        {
            if (!File.Exists(StatsCollector.StatsPath))
                return "[]";
            try
            {
                return File.ReadAllText(StatsCollector.StatsPath);
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Stats, "не прочитался stats.json", ex);
                return "[]";
            }
        }

        /// <summary>
        /// Готовые счётчики по одному трекеру. Их раз в `timeStatsUpdate` минут
        /// считает StatsCron, обходя базу один раз для всех сразу — поэтому
        /// собственный обход базы ради одного трекера не нужен.
        /// </summary>
        public static JObject ForTracker(string trackerName)
        {
            if (string.IsNullOrWhiteSpace(trackerName))
                return null;

            try
            {
                var arr = JArray.Parse(ReadAllJson());

                return arr.OfType<JObject>().FirstOrDefault(i =>
                    string.Equals(i.Value<string>("trackerName"), trackerName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Stats, $"не разобрался stats.json для {trackerName}", ex);
                return null;
            }
        }

        /// <summary>Когда статистика обновлялась в последний раз.</summary>
        public static DateTime? UpdatedAt()
        {
            try
            {
                if (!File.Exists(StatsCollector.StatsMetaPath))
                    return null;

                return JObject.Parse(File.ReadAllText(StatsCollector.StatsMetaPath)).Value<DateTime?>("updatedAt");
            }
            catch (Exception ex)
            {
                JacRedLog.Swallowed(JacRedLogCategories.Stats, "не прочитался stats-meta.json", ex);
                return null;
            }
        }
    }
}
