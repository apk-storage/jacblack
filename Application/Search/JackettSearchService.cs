using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Application.Index;
using JacBlack.Infrastructure.Indexers;
using JacBlack.Models.Api;
using Microsoft.Extensions.Caching.Memory;

namespace JacBlack.Application.Search
{
    public class JackettSearchService : IJackettSearchService
    {
        readonly IFastDbIndex _fastDbIndex;
        readonly ILiveSeeders _liveSeeders;
        readonly Infrastructure.Trackers.Kinozal.KinozalSyncService _kinozal;
        readonly Infrastructure.Trackers.Toloka.TolokaSyncService _toloka;
        readonly Infrastructure.Trackers.Bitru.BitruApiSyncService _bitru;
        readonly ClosedTrackerSeeders _closedTrackers;

        public JackettSearchService(
            IFastDbIndex fastDbIndex,
            ILiveSeeders liveSeeders = null,
            Infrastructure.Trackers.Kinozal.KinozalSyncService kinozal = null,
            Infrastructure.Trackers.Toloka.TolokaSyncService toloka = null,
            Infrastructure.Trackers.Bitru.BitruApiSyncService bitru = null)
        {
            _fastDbIndex = fastDbIndex;
            _liveSeeders = liveSeeders;
            _kinozal = kinozal;
            _toloka = toloka;
            _bitru = bitru;
            _closedTrackers = new ClosedTrackerSeeders(kinozal, toloka, bitru);
        }

        public async Task<List<Result>> SearchAsync(JackettSearchRequest request, IMemoryCache cache, CancellationToken ct = default)
        {
            var q = request.Query;
            bool rqnum = !request.QueryStringValue.Contains("&is_serial=")
                && request.UserAgent == "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36";

            string query = request.QueryText;
            string title = request.Title;
            string title_original = request.TitleOriginal;
            int year = request.Year;
            int is_serial = request.IsSerial;

            if (string.IsNullOrWhiteSpace(query))
                query = IndexerRequestParams.ResolveSearchQuery(q);

            if (string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(title_original))
                return new List<Result>();

            SplitCombinedQuery(ref query, ref title, ref title_original);

            title_original = ResolveCounterpartTitle(query, title, title_original);

            var req = IndexerSearchHelper.BuildRequest(q, request.ApiKey, rqnum, query, title, title_original, year, is_serial);
            var results = await IndexerSearchEngine.SearchCombinedAsync(req, cache, this);
            var filtered = IndexerSearchHelper.ApplyPostFilters(results, q, req);

            // Сиды в базе — снимок на момент индексации, поэтому спрашиваем трекеры
            // о том, что происходит сейчас. Не успели в бюджет — отдаём как есть.
            if (_liveSeeders != null)
                filtered = await _liveSeeders.ApplyAsync(filtered, ct);

            // Закрытые трекеры опросом анонса не берутся: их анонс не отвечает
            // посторонним, а торрент-файлы помечены private, из-за чего не
            // работают ни DHT, ни обмен пирами. Зато у каждого есть свой путь —
            // всё это живёт в общем слое, которым пользуется и выдача сайта.
            var targets = filtered
                .Select(r => new SeedTarget
                {
                    Key = r.Details,
                    Tracker = r.Tracker,
                    Urls = AllUrls(r),
                    Apply = (sid, pir) =>
                    {
                        r.Seeders = sid;
                        r.Peers = pir;

                        if (r.info != null)
                            r.info.seedersLive = true;
                    }
                })
                .ToList();

            await _closedTrackers.ApplyAsync(targets, title_original ?? query, title, ct);

            // Раздачи, которых на трекере уже нет, из выдачи убираем: скачать
            // их нельзя, а число сидов у них — прошлогодний снимок. Признак
            // ставится только по прямому ответу трекера «Тема не найдена».
            filtered = filtered
                .Where(r => !ClosedTrackerSeeders.IsDead(r.Tracker, AllUrls(r)))
                .ToList();

            // Пересортировываем в самом конце, и это важно: список пришёл
            // отсортированным по сидам ДО живого опроса, а опрос числа менял —
            // порядок к этому моменту уже не соответствует показанному.
            //
            // Проверенные идут выше непроверенных при любом раскладе.
            // Непроверенное число — это снимок неизвестной давности, и
            // прошлогодние 96 раздающих не должны стоять над сегодняшними 44.
            // Именно так удалённая раздача «Кода 8» оказывалась первой строкой
            // выдачи (случай 02.08.2026).
            filtered = filtered
                .OrderByDescending(r => r.info != null && r.info.seedersLive)
                .ThenByDescending(r => r.Seeders)
                .ThenByDescending(r => r.Peers)
                .ToList();

            filtered = DropDeadUnlessNothingLeft(filtered);

            // Если код карточки неизвестен — заказываем разовый поход за ним.
            // Он нужен заслону, который разводит тёзок по коду; без кода
            // «Наследники» (Succession) и «Наследники» (Descendants) для нас
            // одно и то же.
            HarvestCardImdb(filtered, title_original, year);

            // То же самое для русского кино: там кода IMDB не существует, и
            // тёзок разводит только Кинопоиск.
            //
            // Ищем в НЕотфильтрованном списке, и это принципиально: код нужен
            // ровно затем, чтобы заслон работал лучше, — требовать, чтобы
            // раздача kinozal уже пережила заслон, значит не добыть код там,
            // где он нужнее всего. Проверено на «Русской жене» 2024: заслон
            // оставлял одну раздачу с rutor, kinozal-раздачи отсеивались, и
            // сборщик не срабатывал ни разу, хотя их в базе полтора десятка.
            HarvestCardKinopoisk(results, title, title_original, year);

            return filtered;
        }

        /// <summary>
        /// Подставляет оригинальное название, если клиент его не прислал.
        ///
        /// Зачем. Индекс базы ищет подстроку по склейке «название :
        /// оригинальное название». У русских трекеров там оба языка, поэтому
        /// они находятся на любом. А у yts, eztv и piratebay оба поля
        /// английские — запрос «Веном» не совпадёт с ними физически. Замер
        /// 31.07.2026: «Веном» — 196 раздач и ни одной от yts, «Venom» — 213,
        /// из них 35 от yts.
        ///
        /// Лампа код IMDB не присылает вовсе (проверено по журналу: ноль из
        /// тридцати двух содержательных запросов) — только название, причём
        /// то русское, то оригинальное. Значит рассчитывать на код нельзя,
        /// и перевод надо делать самим, по накопленному словарю.
        ///
        /// Берём ровно одно название: <see cref="JackettCardMatcher"/>
        /// принимает одно поле, а лишние варианты только размывали бы выдачу.
        /// </summary>
        /// <remarks>
        /// <summary>
        /// Убирает из выдачи заведомо мёртвые раздачи — но никогда не оставляет
        /// человека ни с чем.
        ///
        /// Что считаем мёртвым. Совпадение ДВУХ признаков: обход живости видел
        /// у раздачи ноль раздающих подряд столько раз, сколько задано порогом,
        /// И живой опрос трекеров прямо сейчас тоже дал ноль. Одного счётчика
        /// мало: опрос при поиске воскрешает раздачи постоянно — за прогон
        /// обхода оживает по 1200–1400 записей.
        ///
        /// Почему не удаляем из базы. Ноль на трекере значит «трекер про неё не
        /// знает», а не «файла нет»: раздача живёт на DHT или на трекере,
        /// которого нет в ссылке. Воскрешения это подтверждают.
        ///
        /// Почему нельзя просто вычеркнуть. Замер 06.08.2026 по выборке базы:
        /// порог набрали 39% раздач, и если убирать их безоглядно, **26%
        /// карточек опустеют полностью** — у них живых копий нет вовсе. Пустая
        /// выдача хуже сомнительной: человек хотя бы попробует. Поэтому режем
        /// только там, где после этого что-то остаётся.
        ///
        /// Порог берём общий с обходом живости, чтобы «мёртвая» значило одно
        /// и то же в обоих местах.
        /// </summary>
        internal static List<Result> DropDeadUnlessNothingLeft(List<Result> results)
        {
            if (results == null || results.Count == 0)
                return results;

            int threshold = Math.Max(1, AppInit.conf.sweep?.deadThreshold ?? 5);

            var alive = new List<Result>(results.Count);
            int dropped = 0;

            foreach (var r in results)
            {
                bool мертва = r.info != null
                    && r.info.deadChecks >= threshold
                    && r.Seeders <= 0;

                if (мертва)
                    dropped++;
                else
                    alive.Add(r);
            }

            if (dropped == 0)
                return results;

            // Живых не осталось — отдаём как есть. Пусть человек видит нули
            // и решает сам, чем не видит ничего.
            return alive.Count > 0 ? alive : results;
        }

        /// <summary>
        /// Разбирает склейку двух названий в одной строке запроса.
        ///
        /// Зачем. Кнопка «Уточнить» в Лампе шлёт НЕ поля названий, а одну
        /// строку, склеенную из выбранных частей:
        ///   «Одиссея»                     — работает,
        ///   «Одиссея The Odyssey»         — пусто,
        ///   «The Odyssey Одиссея 2026»    — пусто.
        /// Раздачи с именем «Одиссея The Odyssey» не существует, поэтому поиск
        /// склейки целиком не находит ничего. Проверено логом обращений
        /// 06.08.2026: четыре режима из восьми отдавали пустой ответ в 65 байт.
        ///
        /// Разделитель бывает разный: у одних клиентов « / », у других просто
        /// пробел. Со слэшем работало и раньше, без него — нет.
        ///
        /// Что делаем: разводим строку на русскую и латинскую части и отдаём их
        /// как два названия. Дальше поиск по карточке ищет по обоим — ровно так
        /// же, как когда клиент присылает их полями.
        ///
        /// Строгость от этого не включается: заслон по названиям смотрит на сами
        /// параметры запроса, а их здесь нет. То есть мы улучшаем поиск, но не
        /// начинаем требовать совпадения с тем, что вывели сами.
        /// </summary>
        static void SplitCombinedQuery(ref string query, ref string title, ref string title_original)
        {
            // Только для свободной строки: если клиент прислал названия полями,
            // выводить их из строки незачем.
            if (string.IsNullOrWhiteSpace(query)
                || !string.IsNullOrWhiteSpace(title)
                || !string.IsNullOrWhiteSpace(title_original))
                return;

            string s = query.Trim();

            // Хвостовой год Лампа добавляет отдельным словом. Он приезжает
            // и параметром `year`, поэтому в названии только мешает.
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+(1[89]\d{2}|20\d{2})\s*$", "").Trim();

            string ru, en;

            int slash = s.IndexOf('/');
            if (slash > 0 && slash < s.Length - 1)
            {
                string a = s.Substring(0, slash).Trim();
                string b = s.Substring(slash + 1).Trim();
                ru = HasCyrillic(a) ? a : b;
                en = HasCyrillic(a) ? b : a;
            }
            else
            {
                // Граница письменностей: ищем место, где кириллица сменяется
                // латиницей или наоборот. Разделителем служит пробел, иначе
                // разрежем слово посередине.
                var m = System.Text.RegularExpressions.Regex.Match(s,
                    @"^(?<a>.*?[\p{IsCyrillic}\d\W]*?)\s+(?<b>[A-Za-z].*)$");

                if (!m.Success || !HasCyrillic(m.Groups["a"].Value))
                {
                    // Обратный порядок: сначала латиница, потом кириллица.
                    m = System.Text.RegularExpressions.Regex.Match(s,
                        @"^(?<b>[A-Za-z][^\p{IsCyrillic}]*?)\s+(?<a>[\p{IsCyrillic}].*)$");

                    if (!m.Success)
                        return;
                }

                ru = m.Groups["a"].Value.Trim();
                en = m.Groups["b"].Value.Trim();
            }

            // Обе части должны быть осмысленными и разноязычными — иначе это
            // не склейка, а обычное название из нескольких слов.
            if (ru.Length < 2 || en.Length < 2 || !HasCyrillic(ru) || HasCyrillic(en))
                return;

            title = ru;
            title_original = en;

            // Строку поиска оставляем русской частью: по ней строится выборка
            // из индекса, а латинское название подхватит поиск по карточке.
            query = ru;
        }

        static bool HasCyrillic(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;

            foreach (char c in s)
            {
                if (c >= 'Ѐ' && c <= 'ӿ')
                    return true;
            }

            return false;
        }

        /// Описание выше относится к ResolveCounterpartTitle ниже по файлу:
        /// методы живых сидов вставлены между описанием и самим методом.
        /// </remarks>

        /// <summary>
        /// Одна раздача бывает объединена из нескольких трекеров, и тогда в поле
        /// трекера лежит перечень через запятую. Точное сравнение такие записи
        /// пропускало: из 77 раздач по карточке «Извне» 14 оставались без живого
        /// опроса именно поэтому.
        /// </summary>
        /// <summary>
        /// Все адреса раздачи разом: основной плюс адреса склеенных копий.
        /// Искать идентификатор нужно во всех — у объединённой записи основной
        /// адрес принадлежит лишь одному из трекеров.
        /// </summary>
        /// <summary>
        /// Заказывает добычу кода карточки, если его ещё нет. Берём любую
        /// раздачу rutracker из выдачи — на его страницах ссылка на imdb
        /// стоит почти всегда (проверено: два адреса из трёх).
        /// </summary>
        void HarvestCardImdb(List<Result> results, string titleOriginal, int year)
        {
            if (results == null || results.Count == 0 || string.IsNullOrWhiteSpace(titleOriginal) || year <= 0)
                return;

            foreach (var r in results)
            {
                if (!FromTracker(r, "rutracker"))
                    continue;

                var m = Regex.Match(AllUrls(r), @"https?://[^\s""]*viewtopic\.php\?t=\d+");
                if (!m.Success)
                    continue;

                Infrastructure.Trackers.Rutracker.RutrackerImdbHarvester.EnsureInBackground(
                    m.Value, r.info?.name, titleOriginal, year);
                return;
            }

            // Kinozal вторым источником кодов IMDB не годится, хотя раздача его
            // есть почти в каждой карточке: он ссылается на Кинопоиск, а кода
            // IMDB на его странице нет вовсе — проверено 02.08.2026 на полной
            // странице под входом (33 907 байт, ни одной ссылки на imdb).
            //
            // Заодно выяснилось, кому код вообще нужен: у зарубежных карточек
            // он и так есть у 24–44 раздач из yts и eztv. Без кода остаётся
            // русское и советское кино — а у него кода IMDB нет ни на одном из
            // наших трекеров.
            //
            // Зато у него есть код Кинопоиска, и берём мы его именно оттуда.
        }

        /// <summary>
        /// Заказывает добычу кода Кинопоиска — тем же способом и по той же
        /// причине, что и код IMDB, но для русского кино.
        ///
        /// Почему отдельным проходом, а не вместе с IMDB: коды нужны разным
        /// карточкам. У зарубежной вещи код IMDB и так есть у десятков раздач,
        /// а Кинопоиск ей ничего не добавит; у русской наоборот — IMDB взять
        /// негде, и единственная зацепка это kinozal.
        /// </summary>
        void HarvestCardKinopoisk(List<Result> results, string title, string titleOriginal, int year)
        {
            if (_kinozal == null || results == null || results.Count == 0 || year <= 0)
                return;

            string card = !string.IsNullOrWhiteSpace(titleOriginal) ? titleOriginal : title;
            if (string.IsNullOrWhiteSpace(card))
                return;

            // Уже знаем — не тревожим трекер.
            if (Infrastructure.Persistence.KinopoiskIndex.TryGetByTitle(card, year, out _))
                return;

            string cardKey = Infrastructure.Utils.StringConvert.SearchName(card);
            string titleKey = Infrastructure.Utils.StringConvert.SearchName(title);

            foreach (var r in results)
            {
                if (!FromTracker(r, "kinozal"))
                    continue;

                // Раздача должна принадлежать ИМЕННО этой карточке. Список сюда
                // приходит нефильтрованным — это поиск подстрокой, и в нём лежат
                // посторонние фильмы. Проверено на «Русской жене» 2024: первой
                // раздачей kinozal там оказалась «В плену надежды», и её код
                // 4491006 записался карточке «Русской жены». Такой словарь хуже
                // пустого: он не разводит тёзок, а плодит их.
                //
                // Сверяем строгим равенством, а не похожестью: пропустить
                // добычу — просто не выиграть, записать чужой код — сломать
                // поиск. И год обязателен по той же причине.
                if (r.info == null || r.info.relased != year)
                    continue;

                string n = Infrastructure.Utils.StringConvert.SearchName(r.info.name);
                string o = Infrastructure.Utils.StringConvert.SearchName(r.info.originalname);

                bool совпало =
                    (!string.IsNullOrEmpty(cardKey) && (cardKey == n || cardKey == o))
                    || (!string.IsNullOrEmpty(titleKey) && (titleKey == n || titleKey == o));

                if (!совпало)
                    continue;

                // Разбираем адрес именно kinozal: у склеенной записи в перечне
                // лежат адреса нескольких трекеров, и `details.php?id=` есть не
                // только у него — на этом уже обжигались при отсеве мёртвых,
                // когда id bitru уходил в проверку kinozal.
                var m = Regex.Match(AllUrls(r), @"https?://[^\s""]*kinozal[^\s""]*details\.php\?id=(\d+)");
                if (!m.Success)
                    continue;

                // Подписываем словарь СОБСТВЕННЫМИ названиями раздачи, а не
                // названиями карточки. Причина: оригинальное название клиент
                // присылает не всегда, и тогда служба подставляет его сама —
                // догадкой по словарю. Догадка бывает неверной: карточке
                // «Русская жена» подставилось «В плену надежды», и код лёг под
                // чужим именем (проверено, словарь получался хуже пустого).
                // А названия раздачи мы только что сверили с карточкой — они
                // заведомо те самые.
                Infrastructure.Trackers.Kinozal.KinozalKinopoiskHarvester.EnsureInBackground(
                    _kinozal, m.Groups[1].Value, r.info.name, r.info.originalname, year);
                return;
            }
        }

        /// <summary>
        /// Выбрасывает из выдачи раздачи, удалённые с трекера.
        ///
        /// Их всё равно не скачать: у rutracker торрент-файл лежит только на
        /// самом трекере, а тема удалена вместе с ним. Оставлять такую запись
        /// значит предлагать человеку заведомо мёртвую ссылку — и ещё с
        /// прошлогодним числом раздающих.
        /// </summary>
        static List<Result> DropDeadReleases(List<Result> results)
        {
            if (results == null || results.Count == 0 || Infrastructure.Persistence.DeadReleases.Count == 0)
                return results;

            var kept = new List<Result>(results.Count);

            foreach (var r in results)
            {
                if (FromTracker(r, "rutracker"))
                {
                    var m = Regex.Match(AllUrls(r), @"viewtopic\.php\?t=(\d+)");
                    if (m.Success && Infrastructure.Persistence.DeadReleases.IsDead("rutracker", m.Groups[1].Value))
                        continue;
                }

                if (FromTracker(r, "bitru"))
                {
                    var m = Regex.Match(AllUrls(r), @"details\.php\?id=(\d+)");
                    if (m.Success && Infrastructure.Persistence.DeadReleases.IsDead("bitru", m.Groups[1].Value))
                        continue;
                }

                kept.Add(r);
            }

            return kept;
        }

        static string AllUrls(Result r)
        {
            string main = r?.Details ?? string.Empty;
            var extra = r?.info?.sources;

            return extra == null || extra.Count == 0
                ? main
                : main + " " + string.Join(" ", extra);
        }

        static bool FromTracker(Result r, string tracker)
        {
            string s = r?.Tracker;
            return !string.IsNullOrEmpty(s) && s.Contains(tracker, StringComparison.OrdinalIgnoreCase);
        }

        internal static string ResolveCounterpartTitle(string query, string title, string title_original)
        {
            if (!string.IsNullOrWhiteSpace(title_original))
                return title_original;

            string source = !string.IsNullOrWhiteSpace(query) ? query : title;
            if (string.IsNullOrWhiteSpace(source))
                return title_original;

            var counterparts = JacBlack.Infrastructure.Persistence.ImdbIndex.Counterparts(source);
            return counterparts.Count > 0 ? counterparts[0] : title_original;
        }

        public List<Result> SearchResults(string apikey, string query, string title, string title_original, int year, Dictionary<string, string> category, int is_serial, bool rqnum, IMemoryCache memoryCache)
        {
            string cachekey = $"api:v2.0:indexers:{query}:{title}:{title_original}:{year}:{(category != null && category.Count > 0 ? string.Join(",", category.Select(i => $"{i.Key}={i.Value}")) : "null")}:{is_serial}";
            if (memoryCache != null && memoryCache.TryGetValue(cachekey, out List<Result> _cacheResult))
                return _cacheResult;

            var torrents = JackettCardMatcher.Search(_fastDbIndex, query, title, title_original, year, category, is_serial, rqnum, memoryCache);
            var results = JackettResultBuilder.Build(torrents, apikey, rqnum);

            if (memoryCache != null && AppInit.conf.evercache.enable && AppInit.conf.evercache.validHour == 0)
                memoryCache.Set(cachekey, results, System.DateTime.Now.AddMinutes(5));

            return results;
        }
    }
}
