using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using JacRed.Infrastructure.Parsing;
using JacRed.Models.Details;

namespace JacRed.Infrastructure.Trackers.Rutracker
{
    public static class RutrackerParser
    {
        const string TrackerName = "rutracker";

        public static List<TorrentDetails> ParseTorrentsFromPage(string html, string cat)
        {
            var torrents = new List<TorrentDetails>();

            if (!RutrackerCategories.Map.TryGetValue(cat, out var meta))
                return torrents;

            var document = Parsing.Html.Parse(tParse.ReplaceBadNames(html));

            // Ищем строки по содержимому — по ссылке на тему, — а не по классу
            // оформления `hl-tr`. Класс форум может переименовать при смене шкурки,
            // и разбор молча вернёт пустоту; ссылка на тему есть всегда.
            foreach (var topicLink in document.QuerySelectorAll("a[id^='tt-']"))
            {
                var row = topicLink.Closest("tr") ?? topicLink.ParentElement;
                if (row == null)
                    continue;

                if (!TryParseCreateTime(row, out DateTime createTime))
                    continue;

                if (!TryParseRowFields(row, topicLink, out string url, out string title, out string sid, out string pir, out string sizeName))
                    continue;

                var (name, originalname, relased, skipRow) = ParseTitleNames(meta.TitleKind, title);
                if (skipRow)
                    continue;

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    int.TryParse(sid, out int sidNum);
                    int.TryParse(pir, out int pirNum);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = TrackerName,
                        types = meta.Types,
                        url = url,
                        title = title,
                        sid = sidNum,
                        pir = pirNum,
                        sizeName = sizeName,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
            }

            return torrents;
        }

        public static bool ApplyTopicPageDetails(TorrentDetails t, string fullNews)
        {
            if (fullNews == null)
                return false;

            string time = Regex.Match(fullNews, "<a class=\"p-link small\" href=\"viewtopic.php\\?t=[^\"]+\">([^<]+)</a>").Groups[1].Value;
            DateTime createTime = tParse.ParseCreateTime(time.Replace("-", " "), "dd.MM.yy HH:mm");
            if (createTime != default)
                t.createTime = createTime;

            ApplyImdb(t, fullNews);

            string magnet = Regex.Match(fullNews, "href=\"(magnet:[^\"]+)\" class=\"(med )?magnet-link\"").Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(magnet))
            {
                t.magnet = magnet;
                return true;
            }

            return false;
        }

        static readonly Regex ImdbLink = new Regex(@"imdb\.com/title/(tt\d{6,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Код IMDB со страницы раздачи.
        ///
        /// Зачем он нужен именно отсюда. Код — единственное, чем можно
        /// развести тёзок в поиске по карточке: «Наследники» (Succession)
        /// и «Наследники» (Descendants: Wicked Wonderland) по названиям
        /// неотличимы, русское у обоих совпадает с карточкой дословно.
        /// А в базе код был лишь у той части записей, что пришла от
        /// англоязычных источников: у 34 раздач «Succession» его не было
        /// ни у одной, тогда как у чужих — у всех.
        ///
        /// Здесь же он лежит готовым: оформители рутрекера почти всегда
        /// ставят в шапку прямую ссылку на imdb.com. Страницу мы и так
        /// читаем ради magnet — забрать заодно код ничего не стоит.
        /// </summary>
        static void ApplyImdb(TorrentDetails t, string fullNews)
        {
            if (!string.IsNullOrEmpty(t.imdb))
                return;

            var m = ImdbLink.Match(fullNews);
            if (!m.Success)
                return;

            t.imdb = m.Groups[1].Value.ToLowerInvariant();

            // Кладём в словарь: он потом отвечает на вопрос «какой код
            // у этой карточки», в том числе для раздач, где кода нет.
            Persistence.ImdbIndex.Remember(t.imdb, t.name, t.originalname, t.relased);
        }

        /// <summary>Дата раздачи: «2026-07-06 00:17» отдельным абзацем в строке.</summary>
        static readonly Regex RowDate = new Regex(
            @"^[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}$", RegexOptions.Compiled);

        static readonly Regex TopicId = new Regex(@"^tt-([0-9]+)$", RegexOptions.Compiled);

        static bool TryParseCreateTime(AngleSharp.Dom.IElement row, out DateTime createTime)
        {
            foreach (var p in row.QuerySelectorAll("p"))
            {
                string text = Parsing.Html.Text(p);
                if (RowDate.IsMatch(text) && DateTime.TryParse(text, out createTime) && createTime != default)
                    return true;
            }

            createTime = default;
            return false;
        }

        static bool TryParseRowFields(AngleSharp.Dom.IElement row, AngleSharp.Dom.IElement topicLink, out string url, out string title, out string sid, out string pir, out string sizeName)
        {
            // В заголовках rutracker расставляет <wbr> — точки возможного переноса.
            // Прежний разбор захватывал их вместе с разметкой и вычищал отдельной
            // регуляркой; дерево отдаёт готовый текст сразу без них.
            url = topicLink == null ? string.Empty : TopicId.Match(topicLink.Id ?? string.Empty).Groups[1].Value;
            title = Parsing.Html.Text(topicLink);
            sid = Parsing.Html.Text(row.QuerySelector("span.seedmed b"));
            pir = Parsing.Html.Text(row.QuerySelector("span.leechmed b"));
            sizeName = Parsing.Html.Text(row.QuerySelector("a.dl-stub"));

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(pir) || string.IsNullOrWhiteSpace(sizeName))
            {
                url = title = sid = pir = sizeName = null;
                return false;
            }

            url = $"{AppInit.conf.Rutracker.host}/forum/viewtopic.php?t={url}";
            return true;
        }

        static (string name, string originalname, int relased, bool skipRow) ParseTitleNames(RutrackerTitleKind titleKind, string title)
        {
            return titleKind switch
            {
                RutrackerTitleKind.Movie => ParseMovieTitle(title),
                RutrackerTitleKind.Serial => ParseSerialTitle(title),
                RutrackerTitleKind.NonStandard => ParseNonStandardTitle(title),
                _ => (null, null, 0, false)
            };
        }

        static (string name, string originalname, int relased, bool skipRow) ParseMovieTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

            // Ниже нуля / Bajocero / Below Zero (Йуис Килес / Lluís Quílez) [2021, Испания, боевик, триллер, криминал, WEB-DLRip] MVO (MUZOBOZ) + Original (Spa) + Sub (Rus, Eng)
            var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
            {
                name = g[1].Value;
                originalname = g[2].Value;

                if (int.TryParse(g[3].Value, out int _yer))
                    relased = _yer;
            }
            else
            {
                // Белый тигр / The White Tiger (Рамин Бахрани / Ramin Bahrani) [2021, Индия, США, драма, криминал, WEB-DLRip] MVO (HDRezka Studio) + Sub (Rus, Eng) + Original Eng
                g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                {
                    name = g[1].Value;
                    originalname = g[2].Value;

                    if (int.TryParse(g[3].Value, out int _yer))
                        relased = _yer;
                }
                else
                {
                    // Дневной дозор (Тимур Бекмамбетов) [2006, Россия, боевик, триллер, фэнтези, BDRip-AVC]
                    g = Regex.Match(title, "^([^/\\(\\[]+) \\([^\\)]+\\) \\[([0-9]+), ").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                    {
                        name = g[1].Value;
                        if (int.TryParse(g[2].Value, out int _yer))
                            relased = _yer;
                    }
                }
            }

            if (name != null)
                name = name.Replace("в 3Д", "").Trim();

            if (originalname != null)
                originalname = originalname.Replace(" in 3D", "").Replace(" 3D", "").Trim();

            return (name, originalname, relased, false);
        }

        static (string name, string originalname, int relased, bool skipRow) ParseSerialTitle(string title)
        {
            int relased = 0;
            string name = null, originalname = null;

            if (Regex.IsMatch(title, "(Сезон|Серии)", RegexOptions.IgnoreCase))
            {
                if (title.Contains("Сезон:"))
                {
                    // Голяк / Без гроша / Без денег / Brassic / Сезон: 4 / Серии: 1-8 из 8 (Джон Райт, Дэниэл О'Хара, Сауль Метцштайн, Джон Хардвик) [2022, Великобритания, Комедия, криминал, WEB-DLRip] MVO (Ozz) + Original + Sub (Rus, Ukr, Eng)
                    var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / [^/\\(\\[]+ / ([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Уравнитель / Великий уравнитель / The Equalizer / Сезон: 1 / Серии: 1-3 из 4 (Лиз Фридлендер, Солван Наим) [2021, США, Боевик, триллер, драма, криминал, детектив, WEB-DLRip] MVO (TVShows) + Original
                        g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // 911 служба спасения / 9-1-1 / Сезон: 4 / Серии: 1-6 из 9 (Брэдли Букер, Дженнифер Линч, Гвинет Хердер-Пэйтон) [2021, США, Боевик, триллер, драма, WEB-DLRip] MVO (LostFilm) + Original
                            g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Петербургский роман / Сезон: 1 / Серии: 1-8 из 8 (Александр Муратов) [2018, мелодрама, HDTV 1080i]
                                g = Regex.Match(title, "^([^/\\(\\[]+) / Сезон: [^/]+ / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                                {
                                    name = g[1].Value;
                                    if (int.TryParse(g[2].Value, out int _yer))
                                        relased = _yer;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Уравнитель / Великий уравнитель / The Equalizer / Серии: 1-3 из 4 (Лиз Фридлендер, Солван Наим) [2021, США, Боевик, триллер, драма, криминал, детектив, WEB-DLRip] MVO (TVShows) + Original
                    var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // 911 служба спасения / 9-1-1 / Серии: 1-6 из 9 (Брэдли Букер, Дженнифер Линч, Гвинет Хердер-Пэйтон) [2021, США, Боевик, триллер, драма, WEB-DLRip] MVO (LostFilm) + Original
                        g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Петербургский роман / Серии: 1-8 из 8 (Александр Муратов) [2018, мелодрама, HDTV 1080i]
                            g = Regex.Match(title, "^([^/\\(\\[]+) / [^\\(\\[]+ \\([^\\)]+\\) \\[([0-9]+)(,|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value))
                            {
                                name = g[1].Value;
                                if (int.TryParse(g[2].Value, out int _yer))
                                    relased = _yer;
                            }
                        }
                    }
                }

                if (Regex.IsMatch(name ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase) || Regex.IsMatch(originalname ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase))
                {
                    relased = 0;
                    name = null;
                    originalname = null;
                }
            }

            return (name, originalname, relased, false);
        }

        static (string name, string originalname, int relased, bool skipRow) ParseNonStandardTitle(string title)
        {
            int relased = 0;
            string name = Regex.Match(title, "^([^/\\(\\[]+) ").Groups[1].Value;

            if (int.TryParse(Regex.Match(title, " \\[([0-9]{4})(,|-) ").Groups[1].Value, out int _yer))
                relased = _yer;

            if (Regex.IsMatch(name ?? "", "(Сезон|Серии)", RegexOptions.IgnoreCase))
                return (name, null, relased, true);

            return (name, null, relased, false);
        }
    }
}
