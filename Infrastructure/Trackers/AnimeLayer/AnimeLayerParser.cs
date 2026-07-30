using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.AnimeLayer
{
    public static class AnimeLayerParser
    {
        const string TrackerName = "animelayer";

        static readonly Regex FullDate = new Regex(@"[0-9]+ [^ ]+ [0-9]{4}", RegexOptions.Compiled);

        static readonly Regex ShortDate = new Regex(@"^(.+?) в", RegexOptions.Compiled);

        static readonly Regex TorrentPath = new Regex(@"/?(torrent/[a-z0-9]+)/?", RegexOptions.Compiled);

        static readonly Regex Breaks = new Regex("[\n\r\t]+", RegexOptions.Compiled);

        /// <summary>
        /// Прежний разбор вырезал неразрывные пробелы из ВСЕЙ страницы до начала
        /// работы: разметка animelayer держится на них как на разделителях, и без
        /// этого числа не отделялись от значков. Повторяем ровно то же, но на
        /// отдельных значениях, а не на всём документе.
        /// </summary>
        static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            value = value.Replace(" ", string.Empty);
            return Breaks.Replace(value, " ").Trim();
        }

        static string NumberAfterIcon(AngleSharp.Dom.IElement card, string iconClass)
        {
            var icon = card.QuerySelector("." + iconClass);
            if (icon?.NextSibling == null)
                return string.Empty;

            var m = Regex.Match(Clean(icon.NextSibling.TextContent), "^[0-9]+");
            return m.Success ? m.Value : string.Empty;
        }

        public static List<TorrentDetails> ParseTorrentListFromHtml(string html, string baseHost, int page)
        {
            var torrents = new List<TorrentDetails>();
            var document = Parsing.Html.Parse(tParse.ReplaceBadNames(html));

            foreach (var card in document.QuerySelectorAll(".torrent-item.torrent-item-medium.panel"))
            {
                #region Creation date
                DateTime createTime = default;

                // Дата стоит текстом сразу за подписью «Добавлен:» / «Обновлён:».
                string dateText = string.Empty;
                foreach (var label in card.QuerySelectorAll("span"))
                {
                    string text = Clean(label.TextContent);
                    if (!text.StartsWith("Добавл", StringComparison.Ordinal) && !text.StartsWith("Обновл", StringComparison.Ordinal))
                        continue;

                    dateText = Clean(label.NextSibling?.TextContent);
                    if (!string.IsNullOrWhiteSpace(dateText))
                        break;
                }

                var withYear = FullDate.Match(dateText);
                if (withYear.Success)
                {
                    createTime = tParse.ParseCreateTime(withYear.Value, "dd.MM.yyyy");
                }
                else
                {
                    var short_ = ShortDate.Match(dateText);
                    if (!short_.Success)
                        continue;

                    createTime = tParse.ParseCreateTime($"{short_.Groups[1].Value} {DateTime.Today.Year}", "dd.MM.yyyy");
                }

                if (createTime == default)
                {
                    if (page != 1)
                        continue;

                    createTime = DateTime.UtcNow;
                }
                #endregion

                #region Release data
                var titleLink = card.QuerySelector("a[href^='/torrent/']");

                string urlPath = TorrentPath.Match(Parsing.Html.Attr(titleLink, "href")).Groups[1].Value;
                string title = Clean(titleLink?.TextContent);

                string _sid = NumberAfterIcon(card, "s-icons-upload");
                string _pir = NumberAfterIcon(card, "s-icons-download");

                if (string.IsNullOrWhiteSpace(urlPath) || string.IsNullOrWhiteSpace(title))
                    continue;

                // Match Russian text: "Разрешение" (Resolution)
                if (Regex.IsMatch(card.InnerHtml, "Разрешение: ?</strong>1920x1080"))
                    title += " [1080p]";
                else if (Regex.IsMatch(card.InnerHtml, "Разрешение: ?</strong>1280x720"))
                    title += " [720p]";

                string fullUrl = $"{baseHost}/{urlPath}/";
                #endregion

                #region name / originalname
                string name = null, originalname = null;

                // Example format: "Original Name (2021) / Russian Name [TV] (1-7)"
                var g = Regex.Match(title, "([^/\\[\\(]+)\\([0-9]{4}\\)[^/]+/([^/\\[\\(]+)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                {
                    name = g[2].Value.Trim();
                    originalname = g[1].Value.Trim();
                }
                else
                {
                    // Example format: "Original Name / Russian Name (1—6)"
                    g = Regex.Match(title, "^([^/\\[\\(]+)/([^/\\[\\(]+)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                    {
                        name = g[2].Value.Trim();
                        originalname = g[1].Value.Trim();
                    }
                }
                #endregion

                // Release year (matches Russian text: "Год выхода")
                if (!int.TryParse(Regex.Match(card.InnerHtml, "Год выхода: ?</strong>([0-9]{4})").Groups[1].Value, out int relased) || relased == 0)
                    continue;

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = TrackerName,
                        types = ["anime"],
                        url = fullUrl,
                        title = title,
                        sid = sid,
                        pir = pir,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
            }

            return torrents;
        }
    }
}
