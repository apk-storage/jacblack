using System;
using System.Diagnostics;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Networking;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Infrastructure.Persistence;
using JacBlack.Models.Details;
using Microsoft.Extensions.Caching.Memory;

namespace JacBlack.Infrastructure.Trackers.RuDub
{
    /// <summary>
    /// Обход RuDub.
    ///
    /// Короче соседних служб, и вот почему: у RuDub **торрент отдаётся гостю**
    /// (`download2.php?id=`), поэтому ни входа, ни хранения cookie, ни возни
    /// с протухшей сессией здесь нет. Учётка заведена (`jacblack`) и лежит в
    /// конфиге, но обходу она не нужна — проверено 08.10.2026: под учёткой на
    /// странице 32 раздачи, гостю 31, и торрент качается одинаково.
    ///
    /// Разделов тоже нет: `browse.php` уже отдаёт общий список по всем
    /// категориям, постранично. Это удобнее kinozal с его восемью разделами.
    ///
    /// Инфо-хеш берём из самого файла (`BencodeTo.Magnet`) — ни в списке, ни
    /// на странице раздачи его нет.
    /// </summary>
    public class RuDubSyncService
    {
        const string TrackerName = "rudub";

        /// <summary>
        /// Сколько первых страниц считать «свежими». На странице 31 раздача,
        /// за сутки трекер выкладывает несколько десятков — пяти страниц
        /// хватает с запасом, а весь каталог в 1648 страниц ради новинок
        /// перебирать незачем.
        /// </summary>
        const int LatestPagesDefault = 5;

        readonly IMemoryCache _memoryCache;

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();
        static readonly TrackerLatestParseLock _parseLatestLock = new TrackerLatestParseLock();

        public RuDubSyncService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        public async Task<string> ParseAsync(int page)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: false, async () =>
            {
                var sw = Stopwatch.StartNew();
                int добавлено = 0;

                try
                {
                    добавлено = await ParsePageAsync(page);
                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s)");
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                }

                return $"page {page}: {добавлено}";
            });
        }

        /// <summary>Первые страницы списка — то, что выложено недавно.</summary>
        public async Task<string> ParseLatestAsync(int pages = LatestPagesDefault)
        {
            return await TrackerSyncHelpers.RunParseLatestAsync(TrackerName, _parseLatestLock, checkDisabled: false, async () =>
            {
                var sw = Stopwatch.StartNew();
                int всего = 0;

                for (int page = 1; page <= Math.Max(1, pages); page++)
                {
                    try
                    {
                        всего += await ParsePageAsync(page);
                    }
                    catch (Exception ex)
                    {
                        // Одна страница не должна рвать проход: у соседей это
                        // уже стоило суток молчания.
                        ParserLog.Write(TrackerName, $"Error page={page}: {ex.Message}");
                    }
                }

                ParserLog.Write(TrackerName, $"ИТОГ | страниц={pages} раздач={всего} заняло={sw.Elapsed.TotalSeconds:F1}с");
                return $"latest {pages}: {всего}";
            });
        }

        /// <summary>
        /// Одна страница списка. Возвращает, сколько раздач разобрано.
        /// </summary>
        async Task<int> ParsePageAsync(int page)
        {
            string host = AppInit.conf.RuDub.host;
            string url = $"{host}/browse.php?incldead=0&page={Math.Max(1, page)}";

            ParserLog.Write(TrackerName, $"page {page}: {url}");

            // Страницы в windows-1251. Без явной кодировки кириллица
            // превращается в мусор, и разбор молча находит ноль раздач —
            // именно на этом упали первые тесты.
            string html = await HttpClient.Get(url, encoding: System.Text.Encoding.GetEncoding(1251));
            if (string.IsNullOrWhiteSpace(html) || !html.Contains("card__torlist__browse_2"))
            {
                ParserLog.Write(TrackerName, $"page {page}: страница не похожа на список, пропуск");
                return 0;
            }

            var torrents = RuDubParser.ParseTorrentsFromListPage(html, host);
            if (torrents.Count == 0)
                return 0;

            // Счётчики нужны по существу, а не для отладки: «разобрано 90» и
            // «записано 90» — разные утверждения, и 08.10.2026 они разошлись
            // молча. Без них не видно, на каком шаге раздачи теряются.
            int известных = 0, безФайла = 0, безМагнита = 0, безГода = 0, записано = 0;

            await FileDB.AddOrUpdate(torrents, async (t, db) =>
            {
                // Уже знаем эту раздачу и заголовок не менялся — файл не
                // качаем: инфо-хеш у неё тот же.
                if (db.TryGetValue(t.url, out TorrentDetails _кеш) && _кеш.title == t.title)
                {
                    известных++;
                    return true;
                }

                string torrentUrl = RuDubParser.TorrentUrl(t.url);
                if (string.IsNullOrWhiteSpace(torrentUrl))
                {
                    безФайла++;
                    return false;
                }

                // Год есть только на странице раздачи — в списке его не
                // печатают. Лишний запрос делаем ТОЛЬКО для новых раздач:
                // сюда мы уже дошли, значит эту раздачу видим впервые, и
                // торрент всё равно сейчас качать. Без года запись ложится
                // с нулём, и поиск по году её не находит.
                string страница = await HttpClient.Get(host + "/" + t.url.Split('/')[^1],
                    encoding: System.Text.Encoding.GetEncoding(1251));
                int год = RuDubParser.ParseYear(страница);
                if (год > 0)
                    t.relased = год;
                else
                    безГода++;

                byte[] torrent = await HttpClient.Download(host + torrentUrl, referer: host);
                if (torrent == null || torrent.Length == 0)
                {
                    безФайла++;
                    return false;
                }

                string magnet = BencodeTo.Magnet(torrent);
                if (magnet == null)
                {
                    безМагнита++;
                    return false;
                }

                t.magnet = magnet;
                записано++;
                return true;
            });

            ParserLog.Write(TrackerName,
                $"page {page}: разобрано={torrents.Count} записано={записано} известных={известных} " +
                $"без_файла={безФайла} без_магнита={безМагнита} без_года={безГода}");

            return torrents.Count;
        }
    }
}
