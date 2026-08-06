using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using AngleSharp.Dom;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Models.Details;

namespace JacBlack.Infrastructure.Trackers.Anidub
{
    /// <summary>
    /// Разбор списка раздач anidub. Переведён с регулярок на обход дерева
    /// (AngleSharp) — последним из парсеров, 06.08.2026.
    ///
    /// У anidub ДВА способа вёрстки списка. Новый заворачивает каждую раздачу
    /// в <c>article.story</c>, старый — в <c>div.story</c>. Приоритет тот же,
    /// что был у прежнего парсера: есть article — берём только их (актуальная
    /// вёрстка сайта), иначе старые div. Эталонный снимок покрывает
    /// article-вариант (15 раздач на тестовой странице); старый оставлен как
    /// запасной на случай возврата прежней вёрстки.
    ///
    /// Год для anidub на странице списка не показывается — берётся отдельно
    /// со страницы раздачи в <see cref="ExtractRelased"/>.
    /// </summary>
    public static class AnidubParser
    {
        const string TrackerName = "anidub";

        public const string ValidationDleContent = "dle-content";

        static readonly Regex YearOnDetails = new Regex(
            @"<b>Год:\s*</b>\s*<span>\s*(?:<a[^>]*>)?([0-9]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex DateDigits = new Regex(@"([0-9]{1,2})-([0-9]{2})-([0-9]{4})", RegexOptions.Compiled);

        // Название и оригинал из заголовка «Русское / Original [эпизоды]».
        // Regex здесь уместен: это разбор короткой СТРОКИ, а не вёрстки —
        // за структуру страницы теперь отвечает дерево.
        static readonly Regex TitlePair = new Regex(@"^([^/]+)\s*/\s*([^\[]+)(?:\s*\[|$)", RegexOptions.Compiled);
        static readonly Regex TitleSingle = new Regex(@"^([^\[]+)(?:\s*\[|$)", RegexOptions.Compiled);
        static readonly Regex Whitespace = new Regex(@"[\n\r\t ]+", RegexOptions.Compiled);

        public static int ExtractRelased(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return 0;

            var m = YearOnDetails.Match(html);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int year)
                && year > 1900 && year <= DateTime.UtcNow.Year + 1)
                return year;

            return 0;
        }

        public static List<AnidubDetails> ParseTorrentListFromHtml(string html, string host, int page)
        {
            var torrents = new List<AnidubDetails>();
            if (string.IsNullOrWhiteSpace(html))
                return torrents;

            var document = Parsing.Html.Parse(tParse.ReplaceBadNames(HttpUtility.HtmlDecode(html)));

            // Контейнеры раздач. Новая вёрстка — article.story, старая — div.story.
            // Приоритет article сохраняем, чтобы не задваивать: у article внутри
            // есть свои div (story_h и прочие), и брать их отдельно нельзя.
            var containers = document.QuerySelectorAll("article.story").ToList();
            if (containers.Count == 0)
                containers = document.QuerySelectorAll("div.story").ToList();

            foreach (var box in containers)
            {
                // Ссылка на раздачу — заголовок. У обоих вариантов это первая
                // ссылка на .html внутри блока, обычно в <h2>.
                var link = box.QuerySelector("h2 a[href*='.html']")
                           ?? box.QuerySelectorAll("a[href*='.html']").FirstOrDefault(a => IsRelease(a.GetAttribute("href")));
                if (link == null)
                    continue;

                string urlPath = link.GetAttribute("href") ?? "";
                if (!IsRelease(urlPath))
                    continue;

                string title = Clean(link.TextContent);
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                string fullUrl = urlPath.StartsWith("http") ? urlPath : $"{host}/{urlPath.TrimStart('/')}";

                DateTime createTime = ResolveDate(box, page);
                if (createTime == default)
                    continue;

                var (name, originalname) = SplitTitle(title);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                torrents.Add(new AnidubDetails
                {
                    trackerName = TrackerName,
                    types = TypesFromUrl(urlPath),
                    url = fullUrl,
                    title = title,
                    sid = 1,
                    createTime = createTime,
                    name = name,
                    originalname = originalname,
                    downloadUri = fullUrl,
                });
            }

            return torrents;
        }

        /// <summary>Настоящая ли это ссылка на раздачу, а не на раздел/юзера/поиск.</summary>
        static bool IsRelease(string urlPath)
        {
            if (string.IsNullOrWhiteSpace(urlPath) || !urlPath.Contains(".html"))
                return false;
            if (urlPath.StartsWith("#") || urlPath.Contains("javascript:"))
                return false;
            return !urlPath.Contains("/user/") && !urlPath.Contains("/xfsearch/") && !urlPath.Contains("/forum/");
        }

        /// <summary>
        /// Дата раздачи из блока «Дата: …». Понимает «Сегодня»/«Вчера» и
        /// числовой формат. Не нашли — на первой странице ставим текущее время
        /// (свежая выдача), на последующих раздачу без даты пропускаем: там
        /// подстановка «сейчас» исказила бы порядок по свежести.
        /// </summary>
        static DateTime ResolveDate(IElement box, int page)
        {
            string dateStr = null;
            foreach (var li in box.QuerySelectorAll("li"))
            {
                string t = li.TextContent;
                if (t != null && t.Contains("Дата:"))
                {
                    dateStr = Clean(t.Replace("Дата:", ""));
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(dateStr))
            {
                if (dateStr.Contains("Сегодня"))
                    return DateTime.UtcNow;
                if (dateStr.Contains("Вчера"))
                    return DateTime.UtcNow.AddDays(-1);

                var m = DateDigits.Match(dateStr);
                if (m.Success)
                {
                    string day = m.Groups[1].Value.PadLeft(2, '0');
                    return tParse.ParseCreateTime($"{day}.{m.Groups[2].Value}.{m.Groups[3].Value}", "dd.MM.yyyy");
                }
            }

            return page == 1 ? DateTime.UtcNow : default;
        }

        static (string name, string originalname) SplitTitle(string title)
        {
            var pair = TitlePair.Match(title);
            if (pair.Success)
                return (pair.Groups[1].Value.Trim(), pair.Groups[2].Value.Trim());

            var single = TitleSingle.Match(title);
            if (single.Success)
                return (single.Groups[1].Value.Trim(), null);

            return (Regex.Split(title, @"(\[|\/|\(|\|)")[0].Trim(), null);
        }

        static string[] TypesFromUrl(string urlPath)
        {
            if (urlPath.Contains("/dorama/"))
                return new[] { "dorama" };
            if (urlPath.Contains("/anime_movie/") || urlPath.Contains("/anime-movie/"))
                return new[] { "anime", "movie" };
            if (urlPath.Contains("/anime_ova/") || urlPath.Contains("/anime-ova/"))
                return new[] { "anime", "ova" };
            if (urlPath.Contains("/anime_tv/") || urlPath.Contains("/anime-tv/"))
                return new[] { "anime", "serial" };
            return new[] { "anime" };
        }

        static string Clean(string s) =>
            string.IsNullOrEmpty(s) ? s : Whitespace.Replace(HttpUtility.HtmlDecode(s), " ").Trim();
    }
}
