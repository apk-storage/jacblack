using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Models.Details;

namespace JacBlack.Infrastructure.Trackers.Selezen
{
    public static class SelezenParser
    {
        const string TrackerName = "selezen";

        /// <summary>
        /// Текст сразу за иконкой. У selezen значения не в отдельных ячейках,
        /// а текстом после значка: «&lt;i class="bx bx-chevrons-up"&gt;&lt;/i&gt; 1335».
        /// </summary>
        static string TextAfterIcon(AngleSharp.Dom.IElement card, string iconClass)
        {
            var icon = card.QuerySelector("." + iconClass);
            if (icon?.NextSibling == null)
                return string.Empty;

            return Parsing.Html.Normalize(icon.NextSibling.TextContent);
        }

        public static List<TorrentDetails> ParseTorrentsFromListPage(string html)
        {
            var torrents = new List<TorrentDetails>();

            var document = Parsing.Html.Parse(tParse.ReplaceBadNames(html));

            foreach (var card in document.QuerySelectorAll(".card.overflow-hidden"))
            {
                // Аниме у selezen лежит отдельным разделом и в базу не берётся.
                bool anime = false;
                foreach (var link in card.QuerySelectorAll("a"))
                {
                    if (Parsing.Html.Text(link) == "Аниме")
                    {
                        anime = true;
                        break;
                    }
                }

                if (anime)
                    continue;

                // Значения стоят текстом СРАЗУ ЗА иконкой: <span class="bx bx-calendar"></span> 28.07.2026 21:07
                DateTime createTime = tParse.ParseCreateTime(TextAfterIcon(card, "bx-calendar"), "dd.MM.yyyy HH:mm");
                if (createTime == default) continue;

                var titleNode = card.QuerySelector("h4.card-title");
                var titleLink = titleNode?.Closest("a");

                string url = Parsing.Html.Attr(titleLink, "href");
                string title = Parsing.Html.Text(titleNode);
                if (string.IsNullOrWhiteSpace(url) || !url.Contains(".html", StringComparison.OrdinalIgnoreCase))
                    continue;

                string _sid = TextAfterIcon(card, "bx-chevrons-up");
                string _pir = TextAfterIcon(card, "bx-chevrons-down");
                string sizeName = TextAfterIcon(card, "bx-download");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName))
                    continue;

                int relased = 0;
                string name = null, originalname = null;
                var g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                {
                    name = g[1].Value;
                    originalname = g[2].Value;
                    if (int.TryParse(g[3].Value, out int _yer)) relased = _yer;
                }
                else
                {
                    g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;
                    originalname = g[2].Value;
                    if (int.TryParse(g[3].Value, out int _yer)) relased = _yer;
                }
                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                // Тип: мультфильм по жанру в карточке; сериал по [S01]/[01x01-02 из 09] или TVShows в title/url; иначе movie
                string[] types = new string[] { "movie" };
                if (card.InnerHtml.Contains(">Мульт") || card.InnerHtml.Contains(">мульт"))
                    types = new string[] { "multfilm" };
                else if (title.IndexOf("TVShows", StringComparison.OrdinalIgnoreCase) >= 0
                    || Regex.IsMatch(title, @"\[S\d+\]")
                    || Regex.IsMatch(title, @"\[\d+[xх]\d+")  // 01x01 или 01х01 (латинская/кириллическая х)
                    || (url.IndexOf("tvshows", StringComparison.OrdinalIgnoreCase) >= 0))
                    types = new string[] { "serial" };
                int.TryParse(_sid, out int sid);
                int.TryParse(_pir, out int pir);

                torrents.Add(new TorrentDetails()
                {
                    trackerName = TrackerName,
                    types = types,
                    url = url,
                    title = title,
                    sid = sid,
                    pir = pir,
                    sizeName = sizeName,
                    createTime = createTime,
                    name = name,
                    originalname = originalname,
                    relased = relased
                });
            }

            return torrents;
        }

        public static string ExtractMagnetFromDetailPage(string fullnews)
        {
            if (fullnews == null) return null;
            return Regex.Match(fullnews, "href=\"(magnet:\\?xt=urn:btih:[^\"]+)\"").Groups[1].Value;
        }
    }
}
