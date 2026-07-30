using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web;
using System.Xml.Linq;
using JacRed.Infrastructure.Trackers.AnimeTosho;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Nyaa
{
    /// <summary>
    /// Разбор ленты nyaa.si.
    ///
    /// Лента отдаёт всё нужное сразу: хеш, сиды, размер, дату и раздел —
    /// поэтому magnet собирается на месте, без запроса за torrent-файлом.
    ///
    /// Разбор названия переиспользуется от AnimeTosho: соглашение об именах
    /// там одно и то же («[Группа] Название S02E04 1080p …»), а тот разбор
    /// уже покрыт тестами. Заводить второй такой же было бы ровно тем
    /// дублированием, из-за которого этот код и стал заплаточным.
    /// </summary>
    public static class NyaaParser
    {
        const string TrackerName = "nyaa";

        static readonly XNamespace Ns = "https://nyaa.si/xmlns/nyaa";

        /// <summary>
        /// Трекеры, которые nyaa прописывает в свои torrent-файлы.
        /// Без них magnet живёт только на DHT — так уже было с kinozal
        /// и nnmclub, у которых 262 тысячи ссылок остались без анонсов.
        /// </summary>
        static readonly string[] DefaultTrackers =
        {
            "http://nyaa.tracker.wf:7777/announce",
            "udp://open.stealth.si:80/announce",
            "udp://tracker.opentrackr.org:1337/announce",
            "udp://exodus.desync.com:6969/announce",
            "udp://tracker.torrent.eu.org:451/announce"
        };

        public static List<NyaaItem> ParseFeed(string xml)
        {
            var items = new List<NyaaItem>();
            if (string.IsNullOrWhiteSpace(xml))
                return items;

            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch (System.Xml.XmlException) { return items; }

            foreach (var node in doc.Descendants("item"))
            {
                string hash = (string)node.Element(Ns + "infoHash");
                string title = (string)node.Element("title");

                // Без хеша ссылку не собрать, без названия запись не найти.
                if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(title))
                    continue;

                items.Add(new NyaaItem
                {
                    Title = HttpUtility.HtmlDecode(title).Trim(),
                    ViewUrl = (string)node.Element("guid") ?? (string)node.Element("link"),
                    InfoHash = hash.Trim().ToLowerInvariant(),
                    PubDate = ParseDate((string)node.Element("pubDate")),
                    Seeders = ToInt((string)node.Element(Ns + "seeders")),
                    Leechers = ToInt((string)node.Element(Ns + "leechers")),
                    SizeName = ((string)node.Element(Ns + "size") ?? "").Trim(),
                    CategoryId = ((string)node.Element(Ns + "categoryId") ?? "").Trim(),
                    CategoryName = ((string)node.Element(Ns + "category") ?? "").Trim()
                });
            }

            return items;
        }

        public static List<TorrentDetails> ParseTorrents(IEnumerable<NyaaItem> items)
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

        public static TorrentDetails MapToTorrentDetails(NyaaItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.InfoHash) || string.IsNullOrWhiteSpace(item.Title))
                return null;

            if (string.IsNullOrWhiteSpace(item.ViewUrl))
                return null;

            var parsed = AnimeToshoParser.ParseTitle(item.Title);
            if (string.IsNullOrWhiteSpace(parsed.Name))
                return null;

            return new TorrentDetails
            {
                trackerName = TrackerName,
                types = new[] { "anime" },
                url = item.ViewUrl,
                title = item.Title,
                sid = item.Seeders,
                pir = item.Leechers,
                sizeName = item.SizeName,
                createTime = item.PubDate == default ? DateTime.UtcNow : item.PubDate,
                magnet = BuildMagnet(item),
                name = parsed.Name,
                originalname = parsed.OriginalName,
                relased = parsed.Year
            };
        }

        public static string BuildMagnet(NyaaItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.InfoHash))
                return null;

            string magnet = $"magnet:?xt=urn:btih:{item.InfoHash}";

            if (!string.IsNullOrWhiteSpace(item.Title))
                magnet += "&dn=" + Uri.EscapeDataString(item.Title);

            foreach (string tracker in DefaultTrackers)
                magnet += "&tr=" + Uri.EscapeDataString(tracker);

            return magnet;
        }

        static DateTime ParseDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return default;

            // Лента отдаёт RFC 822: «Wed, 29 Jul 2026 13:56:57 -0000».
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                return dto.UtcDateTime;

            return default;
        }

        static int ToInt(string s) => int.TryParse(s, out int v) ? v : 0;
    }
}
