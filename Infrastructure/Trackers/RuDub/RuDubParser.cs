using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Models.Details;

namespace JacBlack.Infrastructure.Trackers.RuDub
{
    /// <summary>
    /// Разбор списка раздач RuDub.
    ///
    /// Трекер нашёлся 08.10.2026, когда Сергей искал сериал «Кэрри» 2026 и не
    /// нашёл его в jac.black. Движок тот же, что у kinozal (browse.php,
    /// details.php, takelogin.php), но проще: торрент отдаётся гостю, без
    /// авторизации.
    ///
    /// Две особенности, из-за которых разбор не похож на соседние:
    ///
    /// 1. **Года выпуска в списке нет вовсе.** У selezen и прочих он стоит в
    ///    названии в скобках, здесь — нет: карточка показывает только имя,
    ///    сезон, серии и качество. Поэтому `relased` остаётся нулём, и это
    ///    честнее, чем выдумывать год из номера сезона.
    ///
    /// 2. **Инфо-хеша нет ни в списке, ни на странице раздачи** — его берут из
    ///    самого файла через `BencodeTo.Magnet`, как у Bitru и Toloka. Здесь
    ///    парсер только запоминает `download2.php?id=`, качает служба.
    ///
    /// Страницы приходят в windows-1251 — раскодировать обязан вызывающий,
    /// сюда html попадает уже строкой.
    /// </summary>
    public static class RuDubParser
    {
        const string TrackerName = "rudub";

        /// <summary>
        /// Значение из строки списка по её подписи: у RuDub числа стоят не в
        /// отдельных ячейках, а в элементах с атрибутом title — «Дата»,
        /// «Размер», «Активность». Берём текст целиком и чистим от значков.
        /// </summary>
        static string ByTitle(AngleSharp.Dom.IElement card, string title)
        {
            var node = card.QuerySelector("li[title='" + title + "']");
            return node == null ? string.Empty : Parsing.Html.Normalize(node.TextContent);
        }

        /// <param name="host">
        /// Адрес трекера. Нужен, потому что в базу идёт ПОЛНЫЙ адрес раздачи,
        /// а не относительный: он служит ключом записи, и по относительному
        /// «details.php?id=55583» запись не сохраняется вовсе. 08.10.2026 на
        /// этом первый обход разобрал 150 раздач и не положил ни одной.
        /// Так же поступает Rutor и остальные.
        /// </param>
        public static List<TorrentDetails> ParseTorrentsFromListPage(string html, string host = null)
        {
            var torrents = new List<TorrentDetails>();
            if (string.IsNullOrWhiteSpace(html))
                return torrents;

            var document = Parsing.Html.Parse(tParse.ReplaceBadNames(html));

            foreach (var card in document.QuerySelectorAll(".card__torlist__browse_2"))
            {
                // Ссылка на раздачу: details.php?id=55583
                string url = null;
                string title = null;
                foreach (var link in card.QuerySelectorAll("a"))
                {
                    string href = Parsing.Html.Attr(link, "href");
                    if (string.IsNullOrWhiteSpace(href) || !href.Contains("details.php?id=", StringComparison.OrdinalIgnoreCase))
                        continue;

                    url = href;
                    // Название лежит в <b> внутри ссылки, русское и
                    // оригинальное в первой строке, подробности после <br>.
                    var bold = link.QuerySelector("b");
                    title = Parsing.Html.Text(bold ?? link);
                    break;
                }

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title))
                    continue;

                // Полный адрес: он ключ записи в базе.
                if (!string.IsNullOrWhiteSpace(host) && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    url = host.TrimEnd('/') + "/" + url.TrimStart('/');

                // «2026-10-07 21:37:08»
                DateTime createTime = tParse.ParseCreateTime(ByTitle(card, "Дата"), "yyyy-MM-dd HH:mm:ss");
                if (createTime == default)
                    continue;

                string sizeName = ByTitle(card, "Размер");
                if (string.IsNullOrWhiteSpace(sizeName))
                    continue;

                // «Активность» держит ОБА числа разом: сначала раздающие,
                // потом качающие. Отдельных подписей у них нет.
                var активность = Regex.Matches(ByTitle(card, "Активность"), @"\d+");
                int sid = активность.Count > 0 && int.TryParse(активность[0].Value, out int s) ? s : 0;
                int pir = активность.Count > 1 && int.TryParse(активность[1].Value, out int p) ? p : 0;

                var (name, originalname) = SplitTitle(title);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                torrents.Add(new TorrentDetails()
                {
                    trackerName = TrackerName,
                    types = GuessTypes(title),
                    url = url,
                    title = title,
                    sid = sid,
                    pir = pir,
                    sizeName = sizeName,
                    createTime = createTime,
                    name = name,
                    originalname = originalname,
                    // Года в карточке нет — см. заголовок файла.
                    relased = 0
                });
            }

            return torrents;
        }

        /// <summary>
        /// «Морская Полиция: Истоки (NCIS: Origins) Сезон 3 Серии 01 (HD720p
        /// WEBRip)» → имя и оригинальное имя.
        ///
        /// Оригинальное стоит в ПЕРВЫХ скобках, но скобок в строке много —
        /// дальше идут качество, «(Обновляемая)», «(Золото)». Поэтому берём
        /// именно первые и только если внутри есть латиница: «(Золото)» и
        /// «(Обновляемая)» оригинальным названием не являются.
        /// </summary>
        public static (string name, string originalname) SplitTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return (null, null);

            // Подробности после сезона/серий к названию не относятся.
            string head = Regex.Split(title, @"\s*(Сезон|Серии|Серия)\s", RegexOptions.IgnoreCase)[0].Trim();

            var m = Regex.Match(head, @"^(.+?)\s*\(([^)]+)\)\s*$");
            if (!m.Success)
                return (head.Trim(), null);

            string имя = m.Groups[1].Value.Trim();
            string вСкобках = m.Groups[2].Value.Trim();

            // Скобки от имени отрезаем ВСЕГДА, а оригинальным названием
            // считаем только латиницу: «(Интервью с вампиром 3)» — пояснение,
            // а не второе имя, и в поле originalname ему не место, иначе
            // поиск по оригиналу начнёт находить русский текст.
            bool похоже_на_оригинал = Regex.IsMatch(вСкобках, "[A-Za-z]");
            return (имя, похоже_на_оригинал ? вСкобках : null);
        }

        /// <summary>
        /// Сериал или фильм. У RuDub почти всё сериалы — они и озвучивают
        /// сериалы, — но признак берём из самой строки, а не из этого
        /// наблюдения: «Сезон»/«Серии» в названии говорят сами за себя.
        /// </summary>
        public static string[] GuessTypes(string title)
        {
            if (!string.IsNullOrWhiteSpace(title)
                && Regex.IsMatch(title, @"\b(Сезон|Сери[яи])\b", RegexOptions.IgnoreCase))
                return new string[] { "serial" };

            return new string[] { "movie" };
        }

        /// <summary>
        /// Адрес файла торрента по странице раздачи: details.php?id=55583 →
        /// download2.php?id=55583. Именно download2, а не download — на
        /// последний трекер отвечает отказом даже вошедшему.
        /// </summary>
        public static string TorrentUrl(string detailsUrl)
        {
            if (string.IsNullOrWhiteSpace(detailsUrl))
                return null;

            var m = Regex.Match(detailsUrl, @"id=(\d+)");
            return m.Success ? "/download2.php?id=" + m.Groups[1].Value : null;
        }

        /// <summary>Сколько всего страниц в списке — из нумерации внизу.</summary>
        public static int LastPage(string html)
        {
            int last = 1;
            if (string.IsNullOrWhiteSpace(html))
                return last;

            foreach (Match m in Regex.Matches(html, @"page=(\d+)"))
            {
                if (int.TryParse(m.Groups[1].Value, out int n) && n > last)
                    last = n;
            }

            return last;
        }
    }
}
