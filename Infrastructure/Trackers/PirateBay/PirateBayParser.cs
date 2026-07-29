using System;
using System.Collections.Generic;
using System.Web;
using JacRed.Infrastructure.Trackers.Knaben;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.PirateBay
{
    /// <summary>
    /// Разбор ответа apibay.
    ///
    /// Названия там «файловые» — `Obsession.2026.1080p.AMZN.WEB-DL.DDP5.1`, —
    /// то есть ровно такие же, как у Knaben, чей разбор уже написан и покрыт
    /// тестами. Переиспользуем его, а не заводим третью копию той же логики.
    /// </summary>
    public static class PirateBayParser
    {
        const string TrackerName = "piratebay";

        /// <summary>
        /// Трекеры, которые TPB прописывает в свои ссылки. Без них раздача
        /// живёт только на DHT — та же беда, что была у kinozal и nnmclub.
        /// </summary>
        static readonly string[] DefaultTrackers =
        {
            "udp://tracker.opentrackr.org:1337/announce",
            "udp://open.stealth.si:80/announce",
            "udp://tracker.torrent.eu.org:451/announce",
            "udp://exodus.desync.com:6969/announce",
            "udp://open.demonii.com:1337/announce"
        };

        /// <summary>Раздел apibay → наши типы раздач.</summary>
        public static string[] TypesOf(string category)
        {
            switch (category)
            {
                case "201":   // Movies
                case "202":   // Movies DVDR
                case "207":   // HD Movies
                case "209":   // 3D
                    return new[] { "movie" };

                case "205":   // TV shows
                case "208":   // HD TV shows
                    return new[] { "serial" };

                case "206":   // Documentary
                    return new[] { "documovie" };

                default:
                    return null;   // остальные разделы нам не нужны
            }
        }

        public static List<TorrentDetails> ParseItems(IEnumerable<PirateBayItem> items)
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

        public static TorrentDetails MapToTorrentDetails(PirateBayItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.InfoHash))
                return null;

            // Пустой ответ apibay — это одна запись с id «0» и нулевым хешом.
            if (item.Id == "0" || item.InfoHash.TrimStart('0').Length == 0)
                return null;

            var types = TypesOf(item.Category);
            if (types == null)
                return null;

            string title = HttpUtility.HtmlDecode(item.Name).Trim();
            var (name, relased) = KnabenParser.ParseNameAndYear(title);
            if (string.IsNullOrWhiteSpace(name))
                return null;

            long size = ToLong(item.Size);

            return new TorrentDetails
            {
                trackerName = TrackerName,
                types = types,
                url = $"https://thepiratebay.org/description.php?id={item.Id}",
                title = title,
                sid = ToInt(item.Seeders),
                pir = ToInt(item.Leechers),
                size = size,
                sizeName = FormatSize(size),
                createTime = FromUnix(item.Added),
                magnet = BuildMagnet(item.InfoHash, title),
                name = name,
                originalname = name,
                relased = relased
            };
        }

        public static string BuildMagnet(string infoHash, string title)
        {
            if (string.IsNullOrWhiteSpace(infoHash))
                return null;

            string magnet = "magnet:?xt=urn:btih:" + infoHash.ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(title))
                magnet += "&dn=" + Uri.EscapeDataString(title);

            foreach (string tracker in DefaultTrackers)
                magnet += "&tr=" + Uri.EscapeDataString(tracker);

            return magnet;
        }

        static DateTime FromUnix(string raw)
        {
            if (!long.TryParse(raw, out long seconds) || seconds <= 0)
                return DateTime.UtcNow;

            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }

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

        static int ToInt(string s) => int.TryParse(s, out int v) ? v : 0;

        static long ToLong(string s) => long.TryParse(s, out long v) ? v : 0;
    }
}
