using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Persistence;
using JacBlack.Infrastructure.Networking;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Models.Details;

namespace JacBlack.Infrastructure.Trackers.Megapeer
{
    public static class MegapeerParser
    {
        const string BrowsePageValidMarker = "id=\"logo\"";

        static readonly int[] ParseDelayCycleMs = { 30_000, 60_000, 90_000 };
        static int _parseDelayIndex;
        static readonly SemaphoreSlim _browseLock = new SemaphoreSlim(1, 1);

        static int GetNextParseDelayMs()
        {
            int i = Interlocked.Increment(ref _parseDelayIndex) - 1;
            return ParseDelayCycleMs[Math.Abs(i % ParseDelayCycleMs.Length)];
        }

        public static async Task<string> GetMegapeerBrowsePage(string url, string cat)
        {
            await _browseLock.WaitAsync();
            try
            {
                var headers = new List<(string name, string val)>()
                {
                    ("dnt", "1"),
                    ("pragma", "no-cache"),
                    ("referer", $"{AppInit.conf.Megapeer.rqHost()}/cat/{cat}"),
                    ("sec-fetch-dest", "document"),
                    ("sec-fetch-mode", "navigate"),
                    ("sec-fetch-site", "same-origin"),
                    ("sec-fetch-user", "?1"),
                    ("upgrade-insecure-requests", "1")
                };
                const int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    int delayMs = GetNextParseDelayMs();
                    await Task.Delay(delayMs);

                    var (content, response) = await HttpClient.BaseGetAsync(url, encoding: Encoding.GetEncoding(1251), useproxy: AppInit.conf.Megapeer.useproxy, addHeaders: headers);

                    if (!string.IsNullOrEmpty(content) && content.Contains(BrowsePageValidMarker))
                        return content;

                    var status = response?.StatusCode;
                    if (attempt < maxRetries)
                    {
                        ParserLog.Write("megapeer", $"Rate limit or invalid page (status={(int)(status ?? 0)}), retry {attempt}/{maxRetries} after next cycle delay (15/30/45s)");
                        continue;
                    }
                    return null;
                }
                return null;
            }
            finally
            {
                _browseLock.Release();
            }
        }

        public static async Task<bool> ParsePageAsync(string cat, int page)
        {
            string html = await GetMegapeerBrowsePage($"{AppInit.conf.Megapeer.rqHost()}/browse.php?cat={cat}&page={page}", cat);

            var torrents = ParseTorrentsFromPage(html, cat);
            if (torrents.Count == 0)
                return false;

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                if (db.TryGetValue(t.url, out TorrentDetails _tcache) && _tcache.title == t.title)
                    return true;

                byte[] _t = await HttpClient.Download($"{AppInit.conf.Megapeer.host}/download/{t.downloadId}", referer: AppInit.conf.Megapeer.host);
                string magnet = BencodeTo.Magnet(_t);

                if (!string.IsNullOrWhiteSpace(magnet))
                {
                    t.magnet = magnet;
                    return true;
                }

                return false;
            });

            return torrents.Count > 0;
        }


        /// <summary>Ячейка размера: «1.37 GB».</summary>
        static readonly Regex SizeCell = new Regex(
            @"^[0-9]+([.,][0-9]+)?\s*(KB|MB|GB|TB|КБ|МБ|ГБ|ТБ)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Regex Digits = new Regex("[0-9]+", RegexOptions.Compiled);

        /// <summary>Из «/torrent/210402/fejerverki-dnyom» берём «torrent/210402».</summary>
        static readonly Regex TorrentPath = new Regex(@"/?(torrent/[0-9]+)", RegexOptions.Compiled);

        /// <summary>
        /// Число рядом с картинкой-меткой: сиды помечены alt="S", пиры alt="L".
        /// Значение лежит либо в соседнем font, либо просто текстом за картинкой —
        /// у megapeer встречаются оба вида.
        /// </summary>
        static string NumberAfterMarker(AngleSharp.Dom.IElement row, string alt)
        {
            var img = row.QuerySelector($"img[alt='{alt}']");
            if (img == null)
                return string.Empty;

            var next = img.NextElementSibling;
            if (next != null && string.Equals(next.LocalName, "font", StringComparison.OrdinalIgnoreCase))
                return Digits.Match(Parsing.Html.Text(next)).Value;

            var text = img.NextSibling?.TextContent;
            return text == null ? string.Empty : Digits.Match(Parsing.Html.Normalize(text)).Value;
        }
        /// <summary>
        /// Разбор страницы списка, отделённый от загрузки.
        ///
        /// Раньше разбор жил внутри ParsePageAsync вперемешку с запросом и записью
        /// в базу — проверить его снимком страницы было нельзя, и Megapeer
        /// оставался единственным трекером без тестов на разбор.
        ///
        /// Magnet здесь не добывается: он лежит в torrent-файле, за которым
        /// всё равно нужен отдельный запрос.
        /// </summary>
        public static List<MegapeerDetails> ParseTorrentsFromPage(string html, string cat)
        {
            var torrents = new List<MegapeerDetails>();

            if (html == null || !html.Contains(BrowsePageValidMarker))
                return torrents;

            var document = Parsing.Html.Parse(html);

            foreach (var row in document.QuerySelectorAll("tr.table_fon"))
            {
                // Дата стоит в первой ячейке строки. Прежний разбор искал её
                // шаблоном, который требовал определённого вида у СЛЕДУЮЩЕЙ
                // ячейки, и однажды это уже стоило 44 строк из 50.
                DateTime createTime = tParse.ParseCreateTime(
                    Parsing.Html.Text(row.QuerySelector("td")), "dd.MM.yy");

                if (createTime == default)
                    continue;

                var detailsLink = row.QuerySelector("a.url") ?? row.QuerySelector("a[href^='/torrent/']");

                // В адрес берём только номер, без словесного хвоста: так было
                // раньше, и по этому адресу раздачи уже лежат в базе.
                string url = TorrentPath.Match(
                    Parsing.Html.Attr(row.QuerySelector("a[href^='/torrent/']"), "href")).Groups[1].Value;
                string title = Parsing.Html.Text(detailsLink);

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

                if (string.IsNullOrWhiteSpace(title))
                    continue;

                string _sid = NumberAfterMarker(row, "S");
                string _pir = NumberAfterMarker(row, "L");

                url = $"{AppInit.conf.Megapeer.host}/{url}";

                int relased = 0;
                string name = null, originalname = null;

                if (cat == "80")
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
                else if (cat == "79")
                {
                    var g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    name = g[1].Value;
                    if (int.TryParse(g[2].Value, out int _yer))
                        relased = _yer;
                }
                else if (cat == "6")
                {
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
                }
                else if (cat == "5")
                {
                    var g = Regex.Match(title, "^([^/]+) \\[[^\\]]+\\] \\(([0-9]{4})(\\)|-)").Groups;
                    name = g[1].Value;
                    if (int.TryParse(g[2].Value, out int _yer))
                        relased = _yer;
                }
                else if (cat == "55" || cat == "57" || cat == "76")
                {
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
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = Regex.Split(title, "(\\[|\\/|\\(|\\|)", RegexOptions.IgnoreCase)[0].Trim();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    string[] types = Array.Empty<string>();
                    switch (cat)
                    {
                        case "80":
                        case "79":
                            types = new[] { "movie" };
                            break;
                        case "6":
                        case "5":
                            types = new[] { "serial" };
                            break;
                        case "55":
                            types = new[] { "docuserial", "documovie" };
                            break;
                        case "57":
                            types = new[] { "tvshow" };
                            break;
                        case "76":
                            types = new[] { "multfilm", "multserial" };
                            break;
                    }

                    // Идентификатор для скачивания берём из ссылки-картинки в той же строке.
                    string downloadid = Digits.Match(
                        Parsing.Html.Attr(row.QuerySelector("a[href*='download/']"), "href")).Value;

                    if (string.IsNullOrWhiteSpace(downloadid))
                        continue;

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new MegapeerDetails
                    {
                        trackerName = "megapeer",
                        types = types,
                        url = url,
                        title = title,
                        sid = sid,
                        pir = pir,
                        sizeName = sizeName,
                        createTime = createTime,
                        name = name,
                        originalname = originalname,
                        relased = relased,
                        downloadId = downloadid
                    });
                }
            }

            return torrents;
        }
    }
}
