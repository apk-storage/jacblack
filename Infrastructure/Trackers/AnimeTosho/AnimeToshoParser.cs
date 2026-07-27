using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.AnimeTosho
{
    /// <summary>
    /// Разбор заголовков AnimeTosho. Заголовки приходят в виде
    /// "[Группа] Название (год) (Альт. название) - S01E07 [1080p][HEVC]" и десятке
    /// его вариаций, поэтому имя вырезается по самому раннему из маркеров:
    /// сезон-серия, номер серии, год, техническая скобка.
    /// </summary>
    public static class AnimeToshoParser
    {
        const string TrackerName = "animetosho";

        // Ведущие теги релиз-группы: "[Judas] ", "[Erai-raws][Sub] " и т.п.
        static readonly Regex RxGroupPrefix = new Regex(@"^(?:\[[^\]]*\]\s*)+", RegexOptions.Compiled);

        // Год в круглых или квадратных скобках.
        static readonly Regex RxYear = new Regex(@"[\(\[]((?:19|20)\d{2})[\)\]]", RegexOptions.Compiled);

        // " - S01E07", " S01E06" — дефис необязателен: сцена пишет без него.
        static readonly Regex RxSeasonEpisode = new Regex(@"\s+-?\s*S(\d{1,2})E(\d{1,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Качество или источник — тоже граница имени: "Name 1080p AMZN WEB-DL".
        static readonly Regex RxQualityBoundary = new Regex(
            @"\s+(?:\d{3,4}p|\d{3,4}x\d{3,4}|BDRip|BDRemux|BluRay|BRRip|WEB[- ]?DL|WEBRip|WEB|HDTV|DVDRip|TVRip|Remux)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // " S01" перед скобкой или в конце: "Turn A Gundam S01 (1999)"
        static readonly Regex RxSeasonOnly = new Regex(@"\s+S(\d{1,2})(?=\s*[\(\[]|\s*$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // " - 05", " - 05v2" перед скобкой или в конце
        static readonly Regex RxEpisodeOnly = new Regex(@"\s+-\s+(\d{1,3})(?:v\d)?(?=\s*[\(\[]|\s*$)", RegexOptions.Compiled);

        // Признаки того, что скобка техническая, а не альтернативное название.
        static readonly Regex RxTechTokens = new Regex(
            @"\b(?:\d{3,4}p|\d{3,4}x\d{3,4}|BD|BDRip|BDRemux|BluRay|WEB|WEBRip|WEB-DL|HDTV|DVD|DVDRip|TVRip|" +
            @"x26[45]|h\.?26[45]|HEVC|AVC|AV1|AAC|AC3|EAC3|DTS|FLAC|Opus|Vorbis|MP3|10bit|8bit|HDR|Dual[- ]?Audio|" +
            @"Multi[- ]?Sub|Sub(?:s|bed)?|Dub(?:bed)?|Raw|Batch|Uncensored|Remux|REPACK|AMZN|CR|FUNi|NF|HULU)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Результат разбора одного заголовка.</summary>
        public class ParsedTitle
        {
            public string Name { get; set; }
            public string OriginalName { get; set; }
            public int Year { get; set; }
            public int Season { get; set; }
            public int Episode { get; set; }
        }

        /// <summary>
        /// Вытаскивает из сырого заголовка имя, альтернативное имя, год, сезон и серию.
        /// Никогда не возвращает null: если разобрать не удалось, Name — очищенный заголовок.
        /// </summary>
        public static ParsedTitle ParseTitle(string rawTitle)
        {
            var result = new ParsedTitle();
            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                result.Name = "";
                return result;
            }

            string t = HttpUtility.HtmlDecode(rawTitle).Trim();
            t = RxGroupPrefix.Replace(t, "");
            t = t.Trim();

            // Год — первое вхождение четырёхзначного числа в скобках.
            int yearIndex = -1;
            var mYear = RxYear.Match(t);
            if (mYear.Success && int.TryParse(mYear.Groups[1].Value, out int y))
            {
                result.Year = y;
                yearIndex = mYear.Index;
            }

            // Самый ранний маркер, после которого имя заканчивается.
            int cut = t.Length;

            var mSe = RxSeasonEpisode.Match(t);
            if (mSe.Success)
            {
                cut = Math.Min(cut, mSe.Index);
                result.Season = ToInt(mSe.Groups[1].Value);
                result.Episode = ToInt(mSe.Groups[2].Value);
            }

            var mSeason = RxSeasonOnly.Match(t);
            if (mSeason.Success)
            {
                cut = Math.Min(cut, mSeason.Index);
                if (result.Season == 0)
                    result.Season = ToInt(mSeason.Groups[1].Value);
            }

            var mEp = RxEpisodeOnly.Match(t);
            if (mEp.Success)
            {
                cut = Math.Min(cut, mEp.Index);
                if (result.Episode == 0)
                    result.Episode = ToInt(mEp.Groups[1].Value);
            }

            var mQuality = RxQualityBoundary.Match(t);
            if (mQuality.Success)
                cut = Math.Min(cut, mQuality.Index);

            if (yearIndex >= 0)
                cut = Math.Min(cut, yearIndex);

            int bracket = t.IndexOf('[');
            if (bracket > 0)
                cut = Math.Min(cut, bracket);

            string name = t.Substring(0, Math.Max(0, cut)).Trim();
            name = name.Trim(' ', '-', '/', '|', '_', '.');

            // Альтернативное название: скобка сразу после года, если внутри не техника.
            if (yearIndex >= 0)
            {
                string tail = t.Substring(mYear.Index + mYear.Length);
                var mAlt = Regex.Match(tail, @"^\s*\(([^)]{3,})\)");
                if (mAlt.Success)
                {
                    string candidate = mAlt.Groups[1].Value.Trim();
                    if (!RxTechTokens.IsMatch(candidate) && Regex.IsMatch(candidate, "[A-Za-zА-Яа-я]{3,}"))
                        result.OriginalName = candidate;
                }
            }

            if (string.IsNullOrWhiteSpace(name))
                name = t.Trim();

            result.Name = name;
            if (string.IsNullOrWhiteSpace(result.OriginalName))
                result.OriginalName = name;

            return result;
        }

        /// <summary>Преобразует ленту в записи базы. Отбрасывает всё без magnet.</summary>
        public static List<TorrentDetails> ParseTorrents(IEnumerable<AnimeToshoItem> items)
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

        public static TorrentDetails MapToTorrentDetails(AnimeToshoItem item)
        {
            if (item == null)
                return null;

            // Без magnet запись бесполезна, а незавершённые раздачи ещё не имеют файлов.
            if (string.IsNullOrWhiteSpace(item.MagnetUri))
                return null;
            if (!string.IsNullOrWhiteSpace(item.Status) &&
                !item.Status.Equals("complete", StringComparison.OrdinalIgnoreCase))
                return null;

            string rawTitle = !string.IsNullOrWhiteSpace(item.Title) ? item.Title : item.TorrentName;
            if (string.IsNullOrWhiteSpace(rawTitle))
                return null;

            var parsed = ParseTitle(rawTitle);
            if (string.IsNullOrWhiteSpace(parsed.Name))
                return null;

            // Адрес строится из числового id, а не из item.Link со slug:
            // slug на сайте меняется, и запись задваивалась бы. По такому адресу
            // работает и защита от дублей в FileDB.GetTorrentIdFromUrl.
            string url = item.Id > 0
                ? $"https://animetosho.org/view/{item.Id}"
                : item.Link;

            if (string.IsNullOrWhiteSpace(url))
                return null;

            DateTime createTime = item.Timestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(item.Timestamp).UtcDateTime
                : DateTime.UtcNow;

            return new TorrentDetails
            {
                trackerName = TrackerName,
                types = new[] { "anime" },
                url = url,
                title = HttpUtility.HtmlDecode(rawTitle).Trim(),
                sid = item.Seeders ?? 0,
                pir = item.Leechers ?? 0,
                sizeName = FormatSize(item.TotalSize),
                createTime = createTime,
                magnet = item.MagnetUri,
                name = parsed.Name,
                originalname = parsed.OriginalName,
                relased = parsed.Year
            };
        }

        static int ToInt(string s) => int.TryParse(s, out int v) ? v : 0;

        static string FormatSize(long bytes)
        {
            if (bytes <= 0)
                return "0 КБ";
            if (bytes < 1000L * 1024)
                return $"{bytes / 1024.0:F2} КБ";
            if (bytes < 1000L * 1048576)
                return $"{bytes / 1048576.0:F2} МБ";
            if (bytes < 1000L * 1073741824)
                return $"{bytes / 1073741824.0:F2} ГБ";
            return $"{bytes / 1099511627776.0:F2} ТБ";
        }
    }
}
