using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using JacBlack.Infrastructure.Parsing;

namespace JacBlack.Infrastructure.Trackers.Lostfilm
{
    public static partial class LostfilmParser
    {
        public static int ExtractTotalPagesFromNewPageHtml(string html)
        {
            int totalPages = 1;
            if (!string.IsNullOrEmpty(html) && html.Contains("LostFilm.TV"))
            {
                var pageMatches = Regex.Matches(html, @"/new/page_(\d+)");
                for (int i = 0; i < pageMatches.Count; i++)
                    if (int.TryParse(pageMatches[i].Groups[1].Value, out int n) && n > totalPages)
                        totalPages = n;
                if (totalPages > 100)
                    totalPages = 100;
            }
            return totalPages;
        }

        /// <summary>Парсит HTML /new/ и возвращает список с dateStr (как на сайте), relased (год в заголовке), title, url, source.</summary>
        public static List<(string title, string dateStr, int relased, string url, string source)> ParseNewPageDates(string html, string host)
        {
            var result = new List<(string title, string dateStr, int relased, string url, string source)>();
            var sinfoRe = new Regex(@"(\d+)\s*сезон\s*(\d+)\s*серия", RegexOptions.IgnoreCase);
            var dateRe = new Regex(@"(\d{2}\.\d{2}\.\d{4})");

            // Адрес серии: /series/{имя}/season_{N}/episode_{M}/
            var episodeRe = new Regex(@"(/series/([^/""]+)/season_(\d+)/episode_(\d+)/)", RegexOptions.IgnoreCase);

            var document = Parsing.Html.Parse(html);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var link in document.QuerySelectorAll("a[href*='/series/']"))
            {
                var href = episodeRe.Match(Parsing.Html.Attr(link, "href"));
                if (!href.Success)
                    continue;

                string urlPath = href.Groups[1].Value.TrimStart('/');
                string serieName = href.Groups[2].Value;

                // Раньше сюда попадала РАЗМЕТКА внутри ссылки, и сезон с датой
                // искались прямо в ней. Теперь берём текст: тот же смысл, но
                // не рассыпается от вложенных тегов.
                string block = Parsing.Html.Text(link);

                if (string.IsNullOrEmpty(serieName) || seen.Contains(urlPath))
                    continue;
                var sm = sinfoRe.Match(block);
                var dateMatches = dateRe.Matches(block);
                if (!sm.Success || dateMatches.Count == 0)
                    continue;
                string sinfo = HttpUtility.HtmlDecode(Regex.Replace(sm.Value, @"[\s]+", " ").Trim());
                string dateStr = dateMatches[dateMatches.Count - 1].Groups[1].Value;
                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;
                seen.Add(urlPath);
                string originalname = serieName.Replace("_", " ");
                string name = originalname;
                string title = $"{name} / {originalname} / {sinfo} [{relased}]";
                result.Add((title, dateStr, relased, $"{host?.TrimEnd('/')}/{urlPath}", "episode_links"));
            }

            // Плитка «новинки»: <a class="new-movie" href title>, внутри
            // <div class="title"> с номером серии и <div class="date">.
            foreach (var anchor in document.QuerySelectorAll("a.new-movie"))
            {
                // В href бывает и абсолютный адрес — берём путь от /series/.
                var path = Regex.Match(Parsing.Html.Attr(anchor, "href"), @"(/series/.+)$");
                if (!path.Success)
                    continue;

                string urlPath = path.Groups[1].Value.TrimStart('/');
                if (string.IsNullOrEmpty(urlPath) || !urlPath.StartsWith("series/"))
                    continue;

                string nameFromAttr = ShortenSeriesName(Parsing.Html.Attr(anchor, "title"));
                string sinfo = Parsing.Html.Text(anchor.QuerySelector("div.title"));

                // Дат внутри плитки бывает несколько; берём последнюю — так же,
                // как делал прежний разбор.
                var dates = anchor.QuerySelectorAll("div.date");
                string dateStr = dates.Length > 0
                    ? Regex.Match(Parsing.Html.Text(dates[dates.Length - 1]), @"\d{2}\.\d{2}\.\d{4}").Value
                    : string.Empty;

                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;
                string serieName = Regex.Match(urlPath, @"series/([^/]+)(?:/|$)").Groups[1].Value;
                string originalname = serieName.Replace("_", " ");
                string seriesName = !string.IsNullOrWhiteSpace(nameFromAttr) ? nameFromAttr : originalname;
                string title = $"{seriesName} / {originalname} / {sinfo} [{relased}]";
                string fullUrl = $"{host?.TrimEnd('/')}/{urlPath}";
                if (!result.Any(i => i.url == fullUrl))
                    result.Add((title, dateStr, relased, fullUrl, "new-movie"));
            }

            // Список серий. Прежний разбор резал страницу по строке
            // class="hor-breaker dashed" и искал поля в остатке — а это пустой
            // div-РАЗДЕЛИТЕЛЬ, стоящий ПЕРЕД карточкой. Поля брались первым
            // совпадением по всему хвосту документа, то есть держались на том,
            // что следующая карточка идёт дальше по тексту. Настоящий
            // контейнер — div.row, по нему и идём.
            foreach (var row in document.QuerySelectorAll("div.row"))
            {
                // Только относительные ссылки, как и раньше: у карточки серии
                // адрес всегда свой, вида /series/Name/season_1/episode_2/.
                string href = Parsing.Html.Attr(row.QuerySelector("a[href]"), "href");
                string url = href.StartsWith("/") ? href.Substring(1).Trim() : string.Empty;

                string sinfo = Parsing.Html.Text(row.QuerySelector("div.left-part"));
                string name = Parsing.Html.Text(row.QuerySelector("div.name-ru"));
                string originalname = Parsing.Html.Text(row.QuerySelector("div.name-en"));
                string dateStr = Regex.Match(Parsing.Html.Text(row.QuerySelector("div.right-part")), @"\d{2}\.\d{2}\.\d{4}").Value;

                if (string.IsNullOrEmpty(url) || !url.StartsWith("series/") || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(originalname) || string.IsNullOrEmpty(sinfo) || string.IsNullOrEmpty(dateStr))
                    continue;
                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;
                string title = $"{HttpUtility.HtmlDecode(name)} / {HttpUtility.HtmlDecode(originalname)} / {sinfo} [{relased}]";
                string fullUrl = $"{host?.TrimEnd('/')}/{url}";
                if (!result.Any(i => i.url == fullUrl))
                    result.Add((title, dateStr, relased, fullUrl, "hor-breaker"));
            }

            return result;
        }
    }
}
