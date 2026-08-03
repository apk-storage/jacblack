using System;
using System.Collections.Generic;
using JacBlack.Infrastructure.Trackers.Knaben;
using JacBlack.Models.Details;

namespace JacBlack.Infrastructure.Trackers.Eztv
{
    /// <summary>
    /// Разбор ответа EZTV.
    ///
    /// Названия «файловые» — `Show Name S01E02 1080p AMZN WEB-DL x264-GROUP`, —
    /// то есть такие же, как у Knaben и PirateBay, чей разбор уже написан и
    /// покрыт тестами. Переиспользуем его, а не заводим третью копию.
    ///
    /// Всё, что приходит из EZTV, — сериалы: отдельного разделения по типам у
    /// источника нет и не нужно.
    /// </summary>
    public static class EztvParser
    {
        const string TrackerName = "eztv";

        public static List<TorrentDetails> ParseItems(IEnumerable<EztvItem> items)
        {
            var torrents = new List<TorrentDetails>();
            if (items == null)
                return torrents;

            foreach (var item in items)
            {
                var t = MapToTorrentDetails(item);
                if (t != null)
                    torrents.Add(t);
            }

            return torrents;
        }

        public static TorrentDetails MapToTorrentDetails(EztvItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Hash))
                return null;

            string title = item.Title.Trim();

            var (name, relased) = KnabenParser.ParseNameAndYear(title);
            if (string.IsNullOrWhiteSpace(name))
                return null;

            long size = long.TryParse(item.SizeBytes, out long s) ? s : 0;

            return new TorrentDetails
            {
                trackerName = TrackerName,
                types = new[] { "serial" },
                url = $"https://eztvx.to/ep/{item.Id}/",
                title = title,
                sid = item.Seeds,
                pir = item.Peers,
                size = size,
                sizeName = FormatSize(size),
                createTime = FromUnix(item.DateReleasedUnix),
                magnet = string.IsNullOrWhiteSpace(item.MagnetUrl) ? null : item.MagnetUrl,
                name = name,
                originalname = name,
                relased = relased,
                imdb = NormalizeImdb(item.ImdbId),
                seasons = SeasonSet(item.Season)
            };
        }

        /// <summary>
        /// EZTV отдаёт код без префикса — «31596422». Приводим к общепринятому
        /// виду «tt31596422»: именно так его присылает Лампа и так его понимают
        /// TMDB и все прочие.
        /// </summary>
        public static string NormalizeImdb(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim();
            if (raw == "0")
                return null;

            return raw.StartsWith("tt", StringComparison.OrdinalIgnoreCase) ? raw.ToLowerInvariant() : "tt" + raw;
        }

        static HashSet<int> SeasonSet(string season) =>
            int.TryParse(season, out int s) && s > 0 ? new HashSet<int> { s } : null;

        static DateTime FromUnix(long seconds) =>
            seconds <= 0 ? DateTime.UtcNow : DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

        static string FormatSize(long bytes)
        {
            if (bytes <= 0)
                return "";

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return $"{value:0.##} {units[unit]}";
        }
    }
}
