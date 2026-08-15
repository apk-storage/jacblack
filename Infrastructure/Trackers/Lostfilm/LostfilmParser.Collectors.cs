using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Models.Details;

namespace JacBlack.Infrastructure.Trackers.Lostfilm
{
    public static partial class LostfilmParser
    {
        public static Task CollectFromEpisodeLinks(string html, string host, string cookie, List<TorrentDetails> list, int page, Dictionary<string, (string name, string originalname)> horBreakerNameMap = null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var episodeRe = new Regex(@"(/series/([^/""]+)/season_(\d+)/episode_(\d+)/)", RegexOptions.IgnoreCase);
            var sinfoRe = new Regex(@"(\d+)\s*сезон\s*(\d+)\s*серия", RegexOptions.IgnoreCase);
            var dateRe = new Regex(@"(\d{2}\.\d{2}\.\d{4})");

            var document = Parsing.Html.Parse(html);

            foreach (var link in document.QuerySelectorAll("a[href*='/series/']"))
            {
                var href = episodeRe.Match(Parsing.Html.Attr(link, "href"));
                if (!href.Success)
                    continue;

                string urlPath = href.Groups[1].Value.TrimStart('/');
                string serieName = href.Groups[2].Value;

                // Сезон и дату ищем в ТЕКСТЕ ссылки, а не в её разметке.
                string block = Parsing.Html.Text(link);

                if (string.IsNullOrEmpty(serieName) || seen.Contains(urlPath))
                    continue;
                var sm = sinfoRe.Match(block);
                var dateMatches = dateRe.Matches(block);
                if (!sm.Success || dateMatches.Count == 0)
                    continue;

                string dateStr = dateMatches[dateMatches.Count - 1].Groups[1].Value;
                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;
                seen.Add(urlPath);
                string sinfo = HttpUtility.HtmlDecode(Regex.Replace(sm.Value, @"[\s]+", " ").Trim());
                string originalname = serieName.Replace("_", " ");
                string name = originalname;
                if (horBreakerNameMap != null)
                {
                    if (horBreakerNameMap.TryGetValue(urlPath.TrimEnd('/'), out var ruNames)
                        || horBreakerNameMap.TryGetValue("series/" + serieName, out ruNames))
                    {
                        name = ruNames.name;
                        originalname = ruNames.originalname;
                    }
                }
                list.Add(new TorrentDetails
                {
                    trackerName = "lostfilm",
                    types = new[] { "serial" },
                    url = $"{host}/{urlPath}",
                    title = $"{name} / {originalname} / {sinfo} [{relased}]",
                    sid = 1,
                    createTime = createTime,
                    name = name,
                    originalname = originalname,
                    relased = relased
                });
            }
            return Task.CompletedTask;
        }

        public static Task CollectFromNewMovie(string html, string host, string cookie, List<TorrentDetails> list, int page, Dictionary<string, (string name, string originalname)> horBreakerNameMap = null)
        {
            var document = Parsing.Html.Parse(html);

            foreach (var anchor in document.QuerySelectorAll("a.new-movie"))
            {
                // В href бывает и абсолютный адрес — берём путь от /series/.
                var path = Regex.Match(Parsing.Html.Attr(anchor, "href"), @"(/series/.+)$");
                if (!path.Success)
                    continue;

                string urlPath = path.Groups[1].Value.TrimStart('/');
                string nameFromAttr = ShortenSeriesName(Parsing.Html.Attr(anchor, "title"));
                if (string.IsNullOrEmpty(urlPath) || !urlPath.StartsWith("series/") || string.IsNullOrEmpty(nameFromAttr))
                    continue;

                string sinfo = Parsing.Html.Text(anchor.QuerySelector("div.title"));

                // Дат внутри плитки бывает несколько; берём последнюю.
                var dates = anchor.QuerySelectorAll("div.date");
                string dateStr = dates.Length > 0
                    ? Regex.Match(Parsing.Html.Text(dates[dates.Length - 1]), @"\d{2}\.\d{2}\.\d{4}").Value
                    : string.Empty;
                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default && page != 1)
                    continue;
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;

                string serieName = Regex.Match(urlPath, @"series/([^/]+)(?:/|$)").Groups[1].Value;
                if (string.IsNullOrEmpty(serieName))
                    continue;
                string originalname = serieName.Replace("_", " ");
                string seriesName = !string.IsNullOrWhiteSpace(nameFromAttr) ? nameFromAttr : originalname;
                if (horBreakerNameMap != null
                    && (horBreakerNameMap.TryGetValue(urlPath.TrimEnd('/'), out var ruNames)
                        || horBreakerNameMap.TryGetValue("series/" + serieName, out ruNames)))
                {
                    seriesName = ruNames.name;
                    originalname = ruNames.originalname;
                }
                list.Add(new TorrentDetails
                {
                    trackerName = "lostfilm",
                    types = new[] { "serial" },
                    url = $"{host}/{urlPath}",
                    title = $"{seriesName} / {originalname} / {sinfo} [{relased}]",
                    sid = 1,
                    createTime = createTime,
                    name = seriesName,
                    originalname = originalname,
                    relased = relased
                });
            }
            return Task.CompletedTask;
        }

        public static Task CollectFromHorBreaker(string html, string host, string cookie, List<TorrentDetails> list, int page)
        {
            // Настоящий контейнер карточки — div.row; class="hor-breaker dashed"
            // это пустой РАЗДЕЛИТЕЛЬ перед ней, и прежний разбор резал страницу
            // по нему, добирая поля первым совпадением по хвосту документа.
            var document = Parsing.Html.Parse(html);

            foreach (var row in document.QuerySelectorAll("div.row"))
            {
                // Только относительные ссылки, как и раньше.
                string href = Parsing.Html.Attr(row.QuerySelector("a[href]"), "href");
                string url = href.StartsWith("/") ? href.Substring(1).Trim() : string.Empty;

                string sinfo = Parsing.Html.Text(row.QuerySelector("div.left-part"));
                string name = Parsing.Html.Text(row.QuerySelector("div.name-ru"));
                string originalname = Parsing.Html.Text(row.QuerySelector("div.name-en"));
                string dateStr = Regex.Match(Parsing.Html.Text(row.QuerySelector("div.right-part")), @"\d{2}\.\d{2}\.\d{4}").Value;

                if (string.IsNullOrEmpty(url) || !url.StartsWith("series/") || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(originalname) || string.IsNullOrEmpty(sinfo))
                    continue;

                DateTime createTime = tParse.ParseCreateTime(dateStr, "dd.MM.yyyy");
                if (createTime == default && page != 1)
                    continue;
                if (createTime == default)
                    createTime = DateTime.UtcNow;
                int relased = createTime != default ? createTime.Year : 0;
                if (relased <= 0)
                    continue;

                string serieName = Regex.Match(url, @"series/([^/]+)(?:/|$)").Groups[1].Value;
                if (string.IsNullOrEmpty(serieName))
                    continue;
                list.Add(new TorrentDetails
                {
                    trackerName = "lostfilm",
                    types = new[] { "serial" },
                    url = $"{host}/{url}",
                    title = $"{name} / {originalname} / {sinfo} [{relased}]",
                    sid = 1,
                    createTime = createTime,
                    name = HttpUtility.HtmlDecode(name),
                    originalname = HttpUtility.HtmlDecode(originalname),
                    relased = relased
                });
            }
            return Task.CompletedTask;
        }
    }
}
