using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Models.Details;

namespace JacBlack.Infrastructure.Trackers.Rutor
{
    public static class RutorParser
    {
        const string TrackerName = "rutor";

        /// <summary>Ячейка размера: «2.45 GB», «700 MB». Отличает её от соседних.</summary>
        static readonly Regex SizeCell = new Regex(
            @"^[0-9]+([.,][0-9]+)?\s*(KB|MB|GB|TB|КБ|МБ|ГБ|ТБ)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex Digits = new Regex("[0-9]+", RegexOptions.Compiled);

        public static List<TorrentBaseDetails> ParseTorrentsFromPage(string html, string cat)
        {
            var torrents = new List<TorrentBaseDetails>();

            if (!RutorCategories.Map.TryGetValue(cat, out var meta))
                return torrents;

            var document = Parsing.Html.Parse(html);

            foreach (var row in document.QuerySelectorAll("tr.gai, tr.tum"))
            {
                var magnetLink = row.QuerySelector("a[href^='magnet:?xt=urn']");
                if (magnetLink == null)
                    continue;

                // Дата лежит в ячейке слева от той, где кнопка скачивания.
                var downgif = row.QuerySelector("a.downgif");
                var dateCell = downgif?.Closest("td")?.PreviousElementSibling;

                DateTime createTime = tParse.ParseCreateTime(Parsing.Html.Text(dateCell), "dd.MM.yy");
                if (createTime == default)
                    continue;

                var detailsLink = row.QuerySelector("a[href^='/torrent/']");

                string url = Parsing.Html.Attr(detailsLink, "href").TrimStart('/');
                string title = Parsing.Html.Text(detailsLink);
                string _sid = FirstNumber(row.QuerySelector("span.green"));
                string _pir = FirstNumber(row.QuerySelector("span.red"));
                string magnet = Parsing.Html.Attr(magnetLink, "href");

                // Ячеек, выровненных вправо, бывает две: число комментариев и размер.
                // Прежний разбор попадал в нужную по случайности — мешала картинка
                // внутри соседней. Теперь выбираем по существу содержимого.
                string sizeName = string.Empty;
                foreach (var cell in row.QuerySelectorAll("td[align=right]"))
                {
                    string text = Parsing.Html.Text(cell);
                    if (SizeCell.IsMatch(text))
                    {
                        sizeName = text;
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || title.ToLower().Contains("трейлер") || string.IsNullOrWhiteSpace(_sid) || string.IsNullOrWhiteSpace(_pir) || string.IsNullOrWhiteSpace(sizeName) || string.IsNullOrWhiteSpace(magnet))
                    continue;

                if (meta.RequireUkrInTitle && !title.Contains(" UKR"))
                    continue;

                if (title.Contains(" КПК"))
                    continue;

                url = $"{AppInit.conf.Rutor.host}/{url}";

                var (name, originalname, relased) = ParseTitleNames(meta.TitleKind, title);

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                int.TryParse(_sid, out int sid);
                int.TryParse(_pir, out int pir);

                torrents.Add(new TorrentBaseDetails()
                {
                    trackerName = TrackerName,
                    types = meta.Types,
                    url = url,
                    title = title,
                    sid = sid,
                    pir = pir,
                    sizeName = sizeName,
                    magnet = magnet,
                    createTime = createTime,
                    name = name,
                    originalname = originalname,
                    relased = relased
                });
            }

            return torrents;
        }

        /// <summary>
        /// Первое число в узле. Сиды и пиры лежат внутри span вперемешку
        /// с картинкой-стрелкой и неразрывным пробелом.
        /// </summary>
        static string FirstNumber(AngleSharp.Dom.IElement element)
        {
            if (element == null)
                return string.Empty;

            var m = Digits.Match(Parsing.Html.Text(element));
            return m.Success ? m.Value : string.Empty;
        }

        static (string name, string originalname, int relased) ParseTitleNames(RutorTitleKind titleKind, string title)
        {
            return titleKind switch
            {
                RutorTitleKind.ForeignMovie => ParseForeignMovieTitle(title),
                RutorTitleKind.RuMovie => ParseRuMovieTitle(title),
                RutorTitleKind.ForeignSerial => ParseForeignSerialTitle(title),
                RutorTitleKind.RuSerial => ParseRuSerialTitle(title),
                RutorTitleKind.ShowLike => ParseShowLikeTitle(title),
                _ => (null, null, 0)
            };
        }

        static (string name, string originalname, int relased) ParseForeignMovieTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

            var g = Regex.Match(title, "^([^/]+) / ([^/]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
            {
                name = g[1].Value;
                originalname = g[3].Value;

                if (int.TryParse(g[4].Value, out int _yer))
                    relased = _yer;
            }
            else
            {
                g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;

                name = g[1].Value;
                originalname = g[2].Value;

                if (int.TryParse(g[3].Value, out int _yer))
                    relased = _yer;
            }

            return (name, originalname, relased);
        }

        static (string name, string originalname, int relased) ParseRuMovieTitle(string title)
        {
            int relased = 0;
            var g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)").Groups;
            string name = g[1].Value;

            if (int.TryParse(g[2].Value, out int _yer))
                relased = _yer;

            return (name, null, relased);
        }

        static (string name, string originalname, int relased) ParseForeignSerialTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

            var g = Regex.Match(title, "^([^/]+) / [^/]+ / [^/]+ / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
            {
                name = g[1].Value;
                originalname = g[2].Value;

                if (int.TryParse(g[3].Value, out int _yer))
                    relased = _yer;
            }
            else
            {
                g = Regex.Match(title, "^([^/]+) / [^/]+ / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                {
                    name = g[1].Value;
                    originalname = g[2].Value;

                    if (int.TryParse(g[3].Value, out int _yer))
                        relased = _yer;
                }
                else
                {
                    g = Regex.Match(title, "^([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;

                    name = g[1].Value;
                    originalname = g[2].Value;

                    if (int.TryParse(g[3].Value, out int _yer))
                        relased = _yer;
                }
            }

            return (name, originalname, relased);
        }

        static (string name, string originalname, int relased) ParseRuSerialTitle(string title)
        {
            int relased = 0;
            var g = Regex.Match(title, "^([^/]+) \\[[^\\]]+\\] \\(([0-9]{4})(\\)|-)").Groups;
            string name = g[1].Value;

            if (int.TryParse(g[2].Value, out int _yer))
                relased = _yer;

            return (name, null, relased);
        }

        static (string name, string originalname, int relased) ParseShowLikeTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

            if (title.Contains(" / "))
            {
                if (title.Contains("[") && title.Contains("]"))
                {
                    var g = Regex.Match(title, "^([^/]+) / ([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[3].Value;

                        if (int.TryParse(g[4].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        g = Regex.Match(title, "^([^/]+) / ([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;

                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                }
                else
                {
                    var g = Regex.Match(title, "^([^/]+) / ([^/]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[3].Value;

                        if (int.TryParse(g[4].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;

                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                }
            }
            else
            {
                if (title.Contains("[") && title.Contains("]"))
                {
                    var g = Regex.Match(title, "^([^/\\[]+) \\[[^\\]]+\\] +\\(([0-9]{4})(\\)|-)").Groups;
                    name = g[1].Value;

                    if (int.TryParse(g[2].Value, out int _yer))
                        relased = _yer;
                }
                else
                {
                    var g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;

                    if (int.TryParse(g[2].Value, out int _yer))
                        relased = _yer;
                }
            }

            return (name, originalname, relased);
        }
    }
}
