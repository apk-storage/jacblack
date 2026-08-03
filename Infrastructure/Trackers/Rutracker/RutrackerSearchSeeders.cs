using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Networking;

namespace JacBlack.Infrastructure.Trackers.Rutracker
{
    /// <summary>
    /// Живые сиды с rutracker — через его поиск, под входом и через браузер.
    ///
    /// Почему так. Анонс rutracker содержит личный ключ и посторонним не
    /// отвечает, поэтому опрос по протоколу scrape невозможен. Страница
    /// раздачи отдаёт 403. Зато поиск под учётной записью возвращает список
    /// с колонкой сидов — замер 01.08.2026: 50 строк и 50 счётчиков за один
    /// запрос.
    ///
    /// Гостю поиск недоступен: без входа приходит 13 КБ со ссылкой на login
    /// и ни одной строки. Поэтому при пустом ответе делаем вход и повторяем
    /// один раз — сессия браузера живёт дальше и следующим запросам вход уже
    /// не нужен.
    /// </summary>
    public static class RutrackerSearchSeeders
    {
        static readonly Regex Row = new Regex(@"<tr[^>]*class=""[^""]*tCenter[^""]*""[^>]*>(.*?)</tr>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex TopicId = new Regex(@"viewtopic\.php\?t=(\d+)", RegexOptions.Compiled);

        static readonly Regex Seeds = new Regex(@"seedmed[^>]*>\s*(?:<b>\s*)?(\d+)", RegexOptions.Compiled);

        static readonly Regex Leech = new Regex(@"leechmed[^>]*>\s*(?:<b>\s*)?(\d+)", RegexOptions.Compiled);

        #region кеш и фоновое обновление
        sealed class Entry
        {
            public DateTime At;
            public Dictionary<string, (int sid, int pir)> Data;
        }

        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Entry> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _inFlight =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Сколько минут считать сохранённые числа пригодными.</summary>
        const int FreshMinutes = 15;

        /// <summary>
        /// Отдаёт сохранённые счётчики, если они есть. Свежесть не проверяем:
        /// показать числа пятнадцатиминутной давности лучше, чем снимок из
        /// базы, сделанный полгода назад.
        /// </summary>
        public static Dictionary<string, (int sid, int pir)> Cached(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            return _cache.TryGetValue(title.Trim(), out var e) ? e.Data : null;
        }

        /// <summary>
        /// Обновляет счётчики в фоне, уже после того как ответ ушёл.
        ///
        /// В сам поиск этот запрос ставить нельзя: rutracker доступен только
        /// через браузер, и замер 01.08.2026 показал рост времени ответа
        /// с одной секунды до 15–24 при выигрыше в единицы раздач. Здесь же
        /// он никого не задерживает, а к следующему обращению числа готовы.
        ///
        /// Одно название обновляется не чаще раза в четверть часа и не
        /// параллельно самому себе: браузерная сессия одна на всех, и толкаться
        /// в ней означает ронять обходы трекеров.
        /// </summary>
        /// <summary>
        /// Добивает те раздачи карточки, которых не оказалось в выдаче поиска.
        ///
        /// Поиск rutracker показывает не всё: удалённая или перенесённая
        /// раздача в нём не появляется вовсе, и её счётчики остаются в базе
        /// навсегда. Именно так у «Кода 8» держались 96 раздающих при одном
        /// живом (случай 02.08.2026) — раздача t=5820892 удалена, её страница
        /// под входом отдаёт заглушку в 13 КБ вместо 115 КБ у живой.
        ///
        /// Поэтому по остатку идём на страницу раздачи. Живая отдаёт свои
        /// счётчики, мёртвая — заглушку, и тогда честный ответ ноль, а не
        /// прошлогоднее число. Ходим не больше чем по трём за раз: страница
        /// читается через браузер, а сессия одна на всех.
        /// </summary>
        static async Task ProbeMissingAsync(Dictionary<string, (int sid, int pir)> data, IReadOnlyList<string> topicIds)
        {
            if (topicIds == null)
                return;

            int probed = 0;
            foreach (string id in topicIds)
            {
                if (probed >= 3)
                    break;

                if (string.IsNullOrWhiteSpace(id) || data.ContainsKey(id))
                    continue;

                probed++;

                try
                {
                    string html = await CloudflareClearance.FetchAsync($"{AppInit.conf.Rutracker.host}/forum/viewtopic.php?t={id}");

                    if (string.IsNullOrEmpty(html))
                        continue;

                    var s = Seeds.Match(html);
                    var l = Leech.Match(html);

                    if (s.Success)
                    {
                        int.TryParse(s.Groups[1].Value, out int sid);
                        int.TryParse(l.Success ? l.Groups[1].Value : "0", out int pir);
                        data[id] = (sid, pir);
                        continue;
                    }

                    // Обнуляем только когда трекер сам СКАЗАЛ, что темы нет.
                    // Судить по размеру страницы нельзя: короткой она бывает и
                    // когда запрос просто не прошёл, и тогда мы объявили бы
                    // мёртвой живую раздачу. У rutracker признак однозначный —
                    // «Тема не найдена» прямо в тексте страницы.
                    if (html.Contains("Тема не найдена") || html.Contains("Тема не существует"))
                    {
                        data[id] = (0, 0);
                        Persistence.DeadReleases.Remember("rutracker", id);

                        JacBlackLog.Information(JacBlackLogCategories.Trackers,
                            $"rutracker: раздача t={id} удалена с трекера («Тема не найдена»), убрана из выдачи");
                    }
                }
                catch (Exception ex)
                {
                    JacBlackLog.Swallowed(JacBlackLogCategories.Trackers, $"rutracker: страница t={id} не прочитана", ex);
                }
            }
        }

        public static void RefreshInBackground(string[] titles, IReadOnlyList<string> topicIds = null)
        {
            var queries = (titles ?? Array.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (queries.Length == 0)
                return;

            // Ключ — первое название; спрашиваем при этом обо всех.
            string key = queries[0];

            if (_cache.TryGetValue(key, out var e) && DateTime.UtcNow < e.At.AddMinutes(FreshMinutes))
                return;

            if (!_inFlight.TryAdd(key, 0))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var data = await FetchAsync(queries);

                    // Поиск отработал — добиваем то, чего в нём не оказалось.
                    if (data.Count > 0)
                        await ProbeMissingAsync(data, topicIds);

                    if (data.Count > 0)
                        _cache[key] = new Entry { At = DateTime.UtcNow, Data = data };
                }
                catch (Exception ex)
                {
                    JacBlackLog.Swallowed(JacBlackLogCategories.Trackers, $"rutracker: фоновое обновление сидов по «{key}» не удалось", ex);
                }
                finally
                {
                    _inFlight.TryRemove(key, out _);
                }
            });
        }
        #endregion

        /// <summary>
        /// Спрашивает поиск rutracker обо всех названиях карточки и забирает
        /// счётчики со всех страниц выдачи.
        ///
        /// Одной страницы и одного названия не хватало, и это давало самую
        /// заметную людям ложь. Случай 02.08.2026: у «Кода 8» раздача
        /// t=5820892 показывала 96 раздающих при одном живом — она просто не
        /// попала в первые полсотни строк выдачи по слову «Code 8», обновление
        /// её не коснулось, и в Лампу ушло число из базы. Пометку
        /// «не проверено» мы ставили честно, но Лампа её не показывает, поэтому
        /// человек видит именно число.
        ///
        /// Страницы берём ПО ОЧЕРЕДИ и не больше трёх на название: браузерная
        /// сессия одна на всех, и параллельные запросы в неё роняют обходы.
        /// Следующую страницу запрашиваем, только если предыдущая полная —
        /// у карточки с тремя раздачами ходить некуда.
        /// </summary>
        public static async Task<Dictionary<string, (int sid, int pir)>> FetchAsync(params string[] titles)
        {
            var result = new Dictionary<string, (int sid, int pir)>(StringComparer.Ordinal);

            var queries = (titles ?? Array.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (queries.Length == 0)
                return result;

            const int RowsPerPage = 50;
            const int MaxPages = 3;

            string url = $"{AppInit.conf.Rutracker.host}/forum/tracker.php";

            foreach (string title in queries)
            {
                try
                {
                    for (int page = 0; page < MaxPages; page++)
                    {
                        // Спрашиваем именно GET-ом. POST на tracker.php молча
                        // отдаёт обычную страницу трекера без единой строки
                        // выдачи — ни ошибки, ни объяснения; проверено
                        // 02.08.2026: POST 107 КБ и ноль результатов, тот же
                        // запрос GET-ом 271 КБ, «Результатов поиска: 56» и все
                        // 50 счётчиков. Форма с form_token не помогает.
                        // Из-за этого живое обновление сидов rutracker почти
                        // всё время работало вхолостую, а в Лампу уходили
                        // числа из базы — так у «Кода 8» держались 96
                        // раздающих при одном живом.
                        string query = url + "?nm=" + HttpUtility.UrlEncode(title, Encoding.UTF8) + "&o=10&s=2";
                        if (page > 0)
                            query += "&start=" + (page * RowsPerPage);

                        string html = await CloudflareClearance.FetchAsync(query);

                        // Пусто — скорее всего слетел вход: гостю вместо выдачи
                        // приходит страница входа на 13 КБ.
                        if (Count(html) == 0 && page == 0 && await LoginAsync())
                            html = await CloudflareClearance.FetchAsync(query);

                        int rows = 0;
                        foreach (Match row in Row.Matches(html ?? string.Empty))
                        {
                            rows++;
                            string body = row.Groups[1].Value;

                            var id = TopicId.Match(body);
                            if (!id.Success)
                                continue;

                            var s = Seeds.Match(body);
                            var l = Leech.Match(body);

                            int.TryParse(s.Success ? s.Groups[1].Value : "0", out int sid);
                            int.TryParse(l.Success ? l.Groups[1].Value : "0", out int pir);

                            result[id.Groups[1].Value] = (sid, pir);
                        }

                        if (rows < RowsPerPage)
                            break;
                    }
                }
                catch (Exception ex)
                {
                    JacBlackLog.Swallowed(JacBlackLogCategories.Trackers, $"rutracker: живые сиды по «{title}» не получены", ex);
                }
            }

            return result;
        }

        static int Count(string html) => string.IsNullOrEmpty(html) ? 0 : Row.Matches(html).Count;

        static async Task<bool> LoginAsync()
        {
            var login = AppInit.conf.Rutracker?.login;
            if (login == null || string.IsNullOrWhiteSpace(login.u) || string.IsNullOrWhiteSpace(login.p))
                return false;

            // Значения не логируем ни при каком исходе — в журнал попадёт
            // только факт попытки.
            string form = "login_username=" + HttpUtility.UrlEncode(login.u, Encoding.UTF8)
                        + "&login_password=" + HttpUtility.UrlEncode(login.p, Encoding.UTF8)
                        + "&login=" + HttpUtility.UrlEncode("Вход", Encoding.GetEncoding(1251));

            string html = await CloudflareClearance.PostFormAsync($"{AppInit.conf.Rutracker.host}/forum/login.php", form);

            bool ok = !string.IsNullOrEmpty(html) && html.Length > 50_000;
            JacBlackLog.Information(JacBlackLogCategories.Trackers, ok ? "rutracker: вход выполнен" : "rutracker: вход не выполнен");
            return ok;
        }
    }
}
