using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Parsing;

namespace JacRed.Infrastructure.Trackers.NNMClub
{
    /// <summary>
    /// Разбор выдачи tracker.php — страницы поиска по раздачам.
    ///
    /// Зачем понадобилась вторая точка входа, когда есть портал. У nnmclub
    /// жёсткий потолок в 200 результатов на ЛЮБОЙ запрос — так написано на самой
    /// странице: «Результатов поиска: 200 (max: 200)». Поэтому portal.php с
    /// start=1000 отдаёт 302 на тему-заглушку, viewforum обрывается на девятой
    /// сотне, а глубокий обход в 11 380 страниц невозможен в принципе.
    ///
    /// Обходится дроблением: у tracker.php есть выбор форума (f[]), а форумов
    /// 698. Каждый запрос упирается в свои 200, но 698 запросов дают на два
    /// порядка больше, чем портал. Проверено 31.07.2026: внутри одного форума
    /// листается ровно четыре страницы по 50 и на start=200 выдача пустеет.
    ///
    /// Строка выдачи богаче портальной: размер приходит И в байтах, И строкой,
    /// а дата — unix-меткой. Это избавляет от разбора русских дат, на котором
    /// уже терялись записи. Чего в строке НЕТ — magnet и хеша: за ними нужен
    /// отдельный запрос к download.php.
    /// </summary>
    public static class NNMClubTrackerParser
    {
        /// <summary>Сколько строк отдаёт одна страница выдачи.</summary>
        public const int PageSize = 50;

        /// <summary>Потолок результатов на один запрос — ограничение самого сайта.</summary>
        public const int ResultsCap = 200;

        public sealed class Row
        {
            public string TopicId;
            public string DownloadId;
            public string Title;
            public string ForumName;
            public long SizeBytes;
            public string SizeName;
            public int Sid;
            public int Pir;
            public DateTime CreateTime;
        }

        static readonly Regex TopicId = new Regex(@"viewtopic\.php\?t=(\d+)", RegexOptions.Compiled);
        static readonly Regex DownloadId = new Regex(@"download\.php\?id=(\d+)", RegexOptions.Compiled);
        static readonly Regex Digits = new Regex(@"\d+", RegexOptions.Compiled);

        public static List<Row> Parse(string html)
        {
            var rows = new List<Row>();
            if (string.IsNullOrWhiteSpace(html))
                return rows;

            var document = Html.Parse(html);

            foreach (var tr in document.QuerySelectorAll("tr.prow1, tr.prow2"))
            {
                var topicLink = tr.QuerySelector("a.topictitle[href*='viewtopic.php']");
                if (topicLink == null)
                    continue;

                var mTopic = TopicId.Match(Html.Attr(topicLink, "href") ?? string.Empty);
                if (!mTopic.Success)
                    continue;

                string title = Html.Text(topicLink);
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                var cells = tr.QuerySelectorAll("td");
                if (cells.Length < 8)
                    continue;

                var row = new Row
                {
                    TopicId = mTopic.Groups[1].Value,
                    Title = title,
                    ForumName = Html.Text(tr.QuerySelector("a.gen[href*='tracker.php?f=']"))
                };

                var dl = tr.QuerySelector("a[href*='download.php?id=']");
                if (dl != null)
                {
                    var mDl = DownloadId.Match(Html.Attr(dl, "href") ?? string.Empty);
                    if (mDl.Success)
                        row.DownloadId = mDl.Groups[1].Value;
                }

                // Размер лежит в одной ячейке двумя видами: точное число байт
                // внутри <u>, следом человеческая запись. Берём оба: по числу
                // считаем и сортируем, строку показываем.
                var sizeCell = tr.QuerySelector("td u");
                if (sizeCell != null)
                {
                    long.TryParse(Html.Text(sizeCell), NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes);
                    row.SizeBytes = bytes;

                    string full = Html.Text(sizeCell.ParentElement);
                    string human = full.Replace(Html.Text(sizeCell), string.Empty).Trim();
                    row.SizeName = string.IsNullOrWhiteSpace(human) ? null : human;
                }

                row.Sid = FirstNumber(tr.QuerySelector("td.seedmed, td[title='Seeders']"));
                row.Pir = FirstNumber(tr.QuerySelector("td.leechmed, td[title='Leechers']"));
                row.CreateTime = ExtractDate(tr);

                rows.Add(row);
            }

            return rows;
        }

        static int FirstNumber(AngleSharp.Dom.IElement element)
        {
            var m = Digits.Match(Html.Text(element) ?? string.Empty);
            return m.Success && int.TryParse(m.Value, out int v) ? v : 0;
        }

        /// <summary>
        /// Дата добавления приходит unix-меткой рядом с человеческой записью.
        /// Берём именно метку: она однозначна и не зависит ни от языка, ни от
        /// формата, тогда как разбор «31-07-2026» уже подводил на других полях.
        ///
        /// Ячейку выбираем по заголовку, а не перебором. Перебор был бы миной:
        /// размер тоже лежит в теге u и тоже десятизначным числом, так что
        /// раздача около гигабайта (1 000 000 000 байт и выше) прочиталась бы
        /// как дата 2001 года. Запасной путь — ПОСЛЕДНИЙ u в строке: порядок
        /// столбцов ставит дату в конец.
        /// </summary>
        static DateTime ExtractDate(AngleSharp.Dom.IElement tr)
        {
            var cell = tr.QuerySelector("td[title='Торрент-файл добавлен'] u");

            if (cell == null)
            {
                var marks = tr.QuerySelectorAll("td u");
                if (marks.Length > 1)
                    cell = marks[marks.Length - 1];
            }

            if (cell == null)
                return default;

            var m = Digits.Match(Html.Text(cell) ?? string.Empty);
            if (m.Success && long.TryParse(m.Value, out long unix) && unix > 0)
                return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;

            return default;
        }
    }
}
