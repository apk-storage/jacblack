using System;
using System.Collections.Generic;
using System.Linq;
using JacBlack.Models.Details;

namespace JacBlack.Infrastructure.Trackers.Yts
{
    /// <summary>
    /// Разбор ответа YTS.
    ///
    /// Особенность источника: один фильм отдаётся с НЕСКОЛЬКИМИ вариантами
    /// качества, и каждый — отдельная раздача со своим хешем. Поэтому из одного
    /// фильма получается две-четыре записи, различающиеся заголовком.
    ///
    /// Название приходит разобранным — отдельно заголовок, отдельно год, — так
    /// что вытаскивать их регуляркой из строки, как у файловых источников, не
    /// нужно.
    /// </summary>
    public static class YtsParser
    {
        const string TrackerName = "yts";

        /// <summary>
        /// Трекеры, которые YTS кладёт в свои ссылки. Без них раздача живёт
        /// только на DHT — та же беда, что была у kinozal и nnmclub.
        /// </summary>
        static readonly string[] DefaultTrackers =
        {
            "udp://tracker.opentrackr.org:1337/announce",
            "udp://open.stealth.si:80/announce",
            "udp://tracker.torrent.eu.org:451/announce",
            "udp://exodus.desync.com:6969/announce",
            "udp://open.demonii.com:1337/announce"
        };

        public static List<TorrentDetails> ParseMovies(IEnumerable<YtsMovie> movies)
        {
            var torrents = new List<TorrentDetails>();
            if (movies == null)
                return torrents;

            foreach (var movie in movies)
            {
                if (movie?.Torrents == null)
                    continue;

                foreach (var variant in movie.Torrents)
                {
                    var t = MapToTorrentDetails(movie, variant);
                    if (t != null)
                        torrents.Add(t);
                }
            }

            return torrents;
        }

        public static TorrentDetails MapToTorrentDetails(YtsMovie movie, YtsTorrent variant)
        {
            if (movie == null || variant == null)
                return null;

            if (string.IsNullOrWhiteSpace(variant.Hash) || string.IsNullOrWhiteSpace(movie.Title))
                return null;

            string title = BuildTitle(movie, variant);

            // Английское название бывает пустым у неанглоязычных фильмов —
            // тогда оригинальным считаем основной заголовок.
            string originalname = string.IsNullOrWhiteSpace(movie.TitleEnglish) ? movie.Title : movie.TitleEnglish;

            return new TorrentDetails
            {
                trackerName = TrackerName,
                types = new[] { "movie" },
                // Адрес обязан быть РАЗНЫМ у каждого варианта качества: база
                // хранит раздачи по адресу, и с общим адресом варианты одного
                // фильма затирали бы друг друга — из ста фильмов в базу попало
                // бы сто записей вместо двухсот двадцати. Проверено 30.07.2026.
                url = VariantUrl(movie, variant),
                title = title,
                sid = variant.Seeds,
                pir = variant.Peers,
                size = variant.SizeBytes,
                sizeName = FormatSize(variant.SizeBytes),
                createTime = FromUnix(variant.DateUploadedUnix),
                magnet = BuildMagnet(variant.Hash, title),
                name = movie.Title.Trim(),
                originalname = originalname?.Trim(),
                relased = movie.Year,
                imdb = NormalizeImdb(movie.ImdbCode)
            };
        }

        /// <summary>
        /// Страница фильма плюс отметка варианта. Якорь браузер игнорирует, так
        /// что ссылка остаётся рабочей, а для базы адреса становятся разными.
        /// </summary>
        static string VariantUrl(YtsMovie movie, YtsTorrent variant)
        {
            string page = string.IsNullOrWhiteSpace(movie.Url)
                ? $"https://yts.mx/movies/{movie.Id}"
                : movie.Url;

            string mark = string.Join("-", new[] { variant.Quality, variant.Type, variant.VideoCodec }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            return string.IsNullOrEmpty(mark) ? page : $"{page}#{mark}";
        }

        /// <summary>
        /// «Название (2014) 1080p BluRay x264» — качество в заголовке обязательно:
        /// у одного фильма несколько вариантов, и без него они неразличимы.
        /// </summary>
        public static string BuildTitle(YtsMovie movie, YtsTorrent variant)
        {
            string baseTitle = string.IsNullOrWhiteSpace(movie.TitleLong)
                ? $"{movie.Title} ({movie.Year})"
                : movie.TitleLong;

            var parts = new List<string> { baseTitle.Trim() };

            if (!string.IsNullOrWhiteSpace(variant.Quality))
                parts.Add(variant.Quality);

            if (!string.IsNullOrWhiteSpace(variant.Type))
                parts.Add(variant.Type.ToUpperInvariant() == "BLURAY" ? "BluRay" : variant.Type);

            if (!string.IsNullOrWhiteSpace(variant.VideoCodec))
                parts.Add(variant.VideoCodec);

            return string.Join(" ", parts);
        }

        public static string NormalizeImdb(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim().ToLowerInvariant();
            return raw.StartsWith("tt", StringComparison.Ordinal) && raw.Length > 2 ? raw : null;
        }

        public static string BuildMagnet(string hash, string title)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return null;

            string magnet = "magnet:?xt=urn:btih:" + hash.ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(title))
                magnet += "&dn=" + Uri.EscapeDataString(title);

            foreach (string tracker in DefaultTrackers)
                magnet += "&tr=" + Uri.EscapeDataString(tracker);

            return magnet;
        }

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
