using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace JacRed.Infrastructure.Trackers.Lostfilm
{
    public static partial class LostfilmParser
    {
        /// <summary>Парсит HTML страницы InSearch (V/?c=...) и извлекает только 1080p / 2160p torrent-ссылки (без скачивания).</summary>
        public static List<(string torrentUrl, string quality)> ParseVPageQualityLinkUrls(string searchHtml)
        {
            if (string.IsNullOrEmpty(searchHtml) || !searchHtml.Contains("inner-box--link"))
                return new List<(string, string)>();

            var document = Parsing.Html.Parse(searchHtml);
            var results = new List<(string torrentUrl, string quality)>();

            // Прежний шаблон требовал, чтобы <a> шёл вплотную за <div>, без единого
            // пробела, и чтобы внутри ссылки не было ни одного вложенного тега.
            // Любая правка вёрстки на сайте — и ссылки на качество молча исчезали.
            foreach (var link in document.QuerySelectorAll("div.inner-box--link.main > a"))
            {
                string linkText = Parsing.Html.Text(link);
                string quality = Regex.Match(linkText, @"(2160p|1080p)", RegexOptions.IgnoreCase).Groups[1].Value;
                if (string.IsNullOrEmpty(quality))
                    quality = Regex.Match(linkText, @"\b(2160|1080)\b", RegexOptions.IgnoreCase).Groups[1].Value;
                if (string.IsNullOrEmpty(quality))
                    continue;
                quality = NormalizeQuality(quality);
                if (!IsPreferredQuality(quality))
                    continue;
                string torrentUrl = Parsing.Html.Attr(link, "href");
                if (string.IsNullOrEmpty(torrentUrl))
                    continue;
                results.Add((torrentUrl, quality));
            }
            return results;
        }
    }
}
