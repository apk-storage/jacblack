using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using JacBlack.Infrastructure.Parsing;

namespace JacBlack.Infrastructure.Trackers.Nyaa
{
    /// <summary>
    /// Разбор HTML-листинга nyaa.si — в дополнение к ленте.
    ///
    /// Зачем понадобился, хотя лента уже читается. Лента отдаёт ровно сто
    /// страниц свежего и дальше пустые; параметры сортировки она молча
    /// игнорирует, а при поиске вообще не смотрит на номер страницы и всякий
    /// раз возвращает первую. То есть глубже 22 тысяч раздач через неё не
    /// пройти в принципе. HTML-листинг те же параметры понимает: сортировка по
    /// возрастанию открывает ДРУГОЙ конец выдачи (самые старые), а поиск
    /// листается по-настоящему. Это и есть путь вглубь.
    ///
    /// Строка таблицы устроена так (проверено 19.08.2026):
    ///   1 ячейка — раздел, ссылкой вида /?c=1_2;
    ///   2 ячейка — комментарии и ссылка на страницу /view/ID, название в title;
    ///   3 ячейка — ссылки на .torrent и magnet, из magnet берём хеш;
    ///   4 — размер, 5 — дата в data-timestamp, 6 — сиды, 7 — пиры, 8 — скачано.
    /// </summary>
    public static class NyaaListingParser
    {
        static readonly Regex RxInfoHash = new Regex(
            @"btih:([0-9a-fA-F]{40}|[A-Z2-7]{32})", RegexOptions.Compiled);

        static readonly Regex RxViewId = new Regex(@"/view/(\d+)", RegexOptions.Compiled);

        /// <summary>
        /// Записи со страницы. Возвращает пустой список, если таблицы нет:
        /// у nyaa это признак того, что страницы за пределом выдачи.
        /// </summary>
        public static List<NyaaItem> Parse(string html, string host = "https://nyaa.si")
        {
            var items = new List<NyaaItem>();
            if (string.IsNullOrWhiteSpace(html))
                return items;

            var document = Html.Parse(html);

            // Строки раздач помечены классом состояния: default — обычная,
            // success — доверенная, danger — помеченная как подделка. Берём все
            // три: отсев по качеству у нас общий и делается позже.
            var rows = document.QuerySelectorAll("table.torrent-list tbody tr");
            if (rows.Length == 0)
                rows = document.QuerySelectorAll("tbody tr");

            foreach (var row in rows)
            {
                var item = ParseRow(row, host);
                if (item != null)
                    items.Add(item);
            }

            return items;
        }

        static NyaaItem ParseRow(IElement row, string host)
        {
            var cells = row.QuerySelectorAll("td").ToList();
            if (cells.Count < 6)
                return null;

            // Название и адрес страницы. Ссылок на /view/ в строке две — сперва
            // счётчик комментариев, потом сама раздача; нужна та, у которой
            // есть title, иначе в название попадёт число комментариев.
            var titleLink = row.QuerySelectorAll("a[href*='/view/']")
                .FirstOrDefault(a => !string.IsNullOrWhiteSpace(Html.Attr(a, "title")))
                ?? row.QuerySelectorAll("a[href*='/view/']").LastOrDefault();

            if (titleLink == null)
                return null;

            string title = Html.Attr(titleLink, "title");
            if (string.IsNullOrWhiteSpace(title))
                title = Html.Text(titleLink);

            string href = Html.Attr(titleLink, "href");
            var mId = RxViewId.Match(href ?? string.Empty);
            if (!mId.Success || string.IsNullOrWhiteSpace(title))
                return null;

            // Хеш — из magnet-ссылки. Без него запись бесполезна: скачивать
            // .torrent отдельным запросом ради каждой раздачи мы не станем.
            string magnet = row.QuerySelectorAll("a[href^='magnet:']")
                .Select(a => Html.Attr(a, "href"))
                .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));

            var mHash = RxInfoHash.Match(magnet ?? string.Empty);
            if (!mHash.Success)
                return null;

            var category = row.QuerySelector("a[href*='?c=']");
            string categoryId = null;
            if (category != null)
            {
                var mCat = Regex.Match(Html.Attr(category, "href") ?? string.Empty, @"c=([0-9_]+)");
                if (mCat.Success)
                    categoryId = mCat.Groups[1].Value;
            }

            return new NyaaItem
            {
                Title = title,
                ViewUrl = host.TrimEnd('/') + "/view/" + mId.Groups[1].Value,
                InfoHash = mHash.Groups[1].Value,
                SizeName = SizeCell(cells),
                PubDate = DateCell(cells),
                Seeders = NumberCell(cells, fromEnd: 3),
                Leechers = NumberCell(cells, fromEnd: 2),
                CategoryId = categoryId,
                CategoryName = category == null ? null : Html.Attr(category, "title"),
            };
        }

        // Размер — единственная ячейка с единицей измерения, поэтому ищем по
        // содержимому, а не по номеру: у строк с комментариями число ячеек
        // совпадает, но порядок тот же лишь пока nyaa не переставит колонки.
        static string SizeCell(List<IElement> cells)
        {
            foreach (var cell in cells)
            {
                string text = Html.Text(cell);
                if (Regex.IsMatch(text, @"^\d+(\.\d+)?\s*(B|KiB|MiB|GiB|TiB)$", RegexOptions.IgnoreCase))
                    return text;
            }
            return null;
        }

        static DateTime DateCell(List<IElement> cells)
        {
            foreach (var cell in cells)
            {
                string stamp = Html.Attr(cell, "data-timestamp");
                if (!string.IsNullOrWhiteSpace(stamp) && long.TryParse(stamp, out long seconds))
                    return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            }
            return default;
        }

        // Сиды, пиры и «скачано» идут тремя последними ячейками. Считаем с
        // конца: слева число ячеек плавает из-за колонки с комментариями.
        static int NumberCell(List<IElement> cells, int fromEnd)
        {
            int index = cells.Count - fromEnd;
            if (index < 0 || index >= cells.Count)
                return 0;

            string text = Html.Text(cells[index]).Replace(",", string.Empty);
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        }
    }
}
