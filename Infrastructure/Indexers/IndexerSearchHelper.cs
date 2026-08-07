using System;
using JacBlack.Models.Api;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace JacBlack.Infrastructure.Indexers
{
    public static class IndexerSearchHelper
    {
        public static IndexerSearchRequest BuildRequest(
            IQueryCollection query,
            string apikey,
            bool rqnum,
            string boundQuery = null,
            string boundTitle = null,
            string boundTitleOriginal = null,
            int boundYear = 0,
            int boundIsSerial = -1)
        {
            string resolvedQuery = boundQuery;
            if (string.IsNullOrWhiteSpace(resolvedQuery))
                resolvedQuery = IndexerRequestParams.ResolveSearchQuery(query);

            string title = boundTitle ?? query["title"].ToString();
            string titleOriginal = boundTitleOriginal ?? query["title_original"].ToString();
            int year = boundYear > 0 ? boundYear : IndexerRequestParams.YearFromQuery(query);

            int isSerial = boundIsSerial;
            if (query.ContainsKey("is_serial") && int.TryParse(query["is_serial"], out int parsedSerial))
                isSerial = parsedSerial;

            var categories = IndexerRequestParams.CategoriesFromQuery(query);
            isSerial = ApplyCategoryIsSerialHint(isSerial, categories);

            bool cardMode = IndexerRequestParams.IsCardMetadataSearch(
                title,
                titleOriginal,
                query.ContainsKey("is_serial") || boundIsSerial >= 0 ? isSerial : (int?)null,
                categories,
                query["genres"]);

            var trackers = IndexerRequestParams.TrackersFromQuery(query);
            string tracker = trackers.Count > 0 ? trackers[0] : null;

            return new IndexerSearchRequest
            {
                Query = resolvedQuery,
                Title = title,
                TitleOriginal = titleOriginal,
                Year = year,
                IsSerial = isSerial,
                Genres = query["genres"],
                Categories = categories,
                Season = IndexerRequestParams.SeasonFromQuery(query),
                Episode = IndexerRequestParams.EpisodeFromQuery(query),
                Tracker = tracker,
                Trackers = trackers,
                CardMode = cardMode,
                ApiKey = apikey,
                RqNum = rqnum
            };
        }

        /// <summary>Same hint logic as legacy JackettSearchResults category block.</summary>
        public static int ApplyCategoryIsSerialHint(int isSerial, List<int> categories)
        {
            // Only refine when client sent is_serial=0 (Jackett "other"). Do not infer from
            // categories when unset (-1) — Torznab/Jackett often pass cat without is_serial.
            if (isSerial != 0 || categories == null || categories.Count == 0)
                return isSerial;

            string cat = string.Join(",", categories);
            if (cat.Contains("5020") || cat.Contains("2010"))
                return 3;
            if (cat.Contains("5080"))
                return 4;
            if (cat.Contains("5070"))
                return 5;
            if (cat.StartsWith("20"))
                return 1;
            if (cat.StartsWith("50"))
                return 2;
            return isSerial;
        }

        public static string CategoryParam(IQueryCollection query)
        {
            string catParam = query["cat"].ToString();
            if (!string.IsNullOrWhiteSpace(catParam))
                return catParam;

            var cats = IndexerRequestParams.CategoriesFromQuery(query);
            return cats.Count > 0 ? string.Join(",", cats) : "";
        }

        public static List<Result> ApplyPostFilters(
            List<Result> results,
            IQueryCollection query,
            IndexerSearchRequest req,
            string torznabAction = null)
        {
            var settings = IndexerSearchOptions.Resolve();
            string catParam = CategoryParam(query);

            bool applyCatFilter = !req.CardMode && !settings.skipCatFilter && !string.IsNullOrWhiteSpace(catParam);
            if (torznabAction != null)
            {
                int isSerial = IndexerRequestParams.IsSerialFromTorznabAction(torznabAction);
                if (query.ContainsKey("is_serial") && int.TryParse(query["is_serial"], out int parsedSerial))
                    isSerial = parsedSerial;
                applyCatFilter = applyCatFilter && isSerial < 0;
            }

            if (applyCatFilter)
                results = IndexerResultFilters.FilterByCategory(results, catParam);

            if (req.Year > 0 && !req.CardMode)
                results = IndexerResultFilters.FilterByYear(results, req.Year);

            if (req.Season.HasValue)
                results = SeasonEpisodeFilter.Filter(results, req.Season.Value, req.Episode);

            results = IndexerResultFilters.FilterByTrackers(results, req.Trackers);

            // Заслон включается только когда клиент ДЕЙСТВИТЕЛЬНО прислал
            // название карточки — то есть в запросе есть параметр
            // title_original.
            //
            // Ни поля названий, ни CardMode для этого не годятся: свободный
            // запрос подставляется и в Title, и в TitleOriginal, а CardMode
            // оказывается истинным и для обычного поиска. Проверено обоими
            // способами — «Извне» одной строкой падало со 114 раздач до 7.
            // Наличие самого параметра подделать нечему: Лампа его шлёт,
            // строка поиска — нет.
            // Лампа умеет искать восемью способами: оригинал, русское, оба —
            // и каждый с годом или без. Признаком служит любой из присланных
            // параметров названия: свободный поиск с сайта шлёт только Query
            // и сюда не попадает.
            // Лампа ищет восемью способами: оригинал, русское, оба — каждый
            // с годом или без. Смотрим, что клиент прислал НА САМОМ ДЕЛЕ.
            //
            // Различать обязательно: оригинальное название нам ещё и
            // подставляет словарь соответствий, когда клиент его не слал.
            // Такое синтетическое название требовать нельзя — на этом режим
            // «Русский» падал со 115 раздач до 2, а «Русский + Год» до нуля.
            bool originalGiven = query.ContainsKey("title_original") && !string.IsNullOrWhiteSpace(query["title_original"]);
            bool titleGiven = query.ContainsKey("title") && !string.IsNullOrWhiteSpace(query["title"]);

            // Кнопка «Уточнить» в Лампе названий полями не шлёт — она склеивает
            // их в строку запроса («Одиссея The Odyssey 2026»). Такую склейку мы
            // разбираем на две части заранее, и раз она разобралась, запрос
            // заведомо пришёл из карточки: человек руками два названия подряд
            // не набирает. Значит и заслон карточки тут уместен — иначе в эти
            // режимы лезет лишнее, вроде фильма о съёмках «The Odyssey: The
            // Making of an Epic».
            //
            // Отличаем от опасного случая: словарь соответствий подставляет
            // ОДНО название, оригинальное, когда клиент его не слал. Требовать
            // совпадения с подставленным нельзя — на этом режим «Русский» падал
            // со 115 раздач до 2. Здесь же заполнены ОБА, а так бывает только
            // после разбора склейки.
            bool derivedFromQuery = !originalGiven && !titleGiven
                && !string.IsNullOrWhiteSpace(req.Title)
                && !string.IsNullOrWhiteSpace(req.TitleOriginal);

            if (originalGiven || titleGiven || derivedFromQuery)
            {
                results = FilterByCardTitle(results, req,
                    originalGiven || derivedFromQuery, titleGiven || derivedFromQuery);
            }
            else if (req.Year > 0)
            {
                // Названий не прислали — сверять нечего, но год работать обязан.
                // Он проверяется и выше (строка `req.Year > 0 && !req.CardMode`),
                // однако `CardMode` истинен и для обычного поиска, поэтому та
                // проверка молча не срабатывала: «Матрица» с годом 1812 отдавала
                // те же 462 раздачи, что и без года вовсе.
                var byYear = new List<Result>(results.Count);
                foreach (var r in results)
                {
                    if (YearFits(r, req.Year, req.IsSerial))
                        byYear.Add(r);
                }

                results = byYear;
            }

            var (limit, offset) = IndexerRequestParams.LimitOffsetFromQuery(query);
            var page = IndexerResultFilters.Paginate(results, limit, offset);

            // Приводим запись сезона в заголовке к форме «N сезон» — иначе Лампа
            // не раскладывает по сезонам раздачи, где номер стоит в хвосте
            // («…(сезон 3, серии 1-8)»), и они пропадают из её списка, хотя
            // выдаём мы их исправно. Перестановка идемпотентна.
            if (page != null)
                foreach (var r in page)
                    if (r != null)
                        r.Title = SeasonTitleNormalizer.Normalize(r.Title);

            return page;
        }

        /// <summary>
        /// Отсекает чужое, когда искали по карточке.
        ///
        /// Зачем понадобился общий заслон. Выдача собирается из нескольких
        /// веток — точный поиск по карточке, запасной обычный поиск, поиск по
        /// парам названий, — и мягкое сопоставление хотя бы в одной из них
        /// тянет в ответ посторонние раздачи. У сериала «Извне» оригинальное
        /// название «From», обычное английское слово: замер 31.07.2026 дал 114
        /// верных раздач по русскому названию и 1381 вместе с оригинальным,
        /// где были и «The Most Heretical Last Boss Queen: From Villainess»,
        /// и «Rakudai Kenja no Gakuin Musou». Чинить каждую ветку по отдельности
        /// значило бы ловить одно и то же в трёх местах и однажды пропустить
        /// четвёртое.
        ///
        /// Правило простое: раз клиент прислал название карточки, раздача
        /// обязана называться так же — совпадать с начала, а не упоминать
        /// название где-то внутри.
        /// </summary>
        internal static List<Result> FilterByCardTitle(List<Result> results, IndexerSearchRequest req, bool originalGiven, bool titleGiven)
        {
            if (results == null || results.Count == 0)
                return results;

            // Заслон включается только для настоящей карточки — по присланному
            // оригинальному названию. Одного русского мало: свободный запрос из
            // строки поиска подставляется в то же поле, и строгость по нему
            // выбрасывала англоязычные раздачи. Замер: «Извне» одним запросом
            // давал 114 раздач, а с включённым заслоном — 7.
            // Берём только то, что клиент прислал сам. Подставленное словарём
            // в счёт не идёт — иначе потребуем совпадения с догадкой.
            string en = originalGiven ? JacBlack.Infrastructure.Utils.StringConvert.SearchName(req.TitleOriginal) : null;
            string ru = titleGiven ? JacBlack.Infrastructure.Utils.StringConvert.SearchName(req.Title) : null;

            if (string.IsNullOrEmpty(en) && string.IsNullOrEmpty(ru))
                return results;

            // Код IMDB надёжнее любой строки: одноимённые вещи он разводит
            // сразу. Карточку сводим к коду по паре «оригинальное название +
            // год» — обратный поиск для этого уже есть.
            //
            // Записи БЕЗ кода не выбрасываем: он известен примерно у 40% базы,
            // и строгость по нему выкосила бы большую часть верной выдачи.
            // Их проверяет прежнее правило по названиям.
            string cardImdb = null;
            if (req.Year > 0)
                cardImdb = ResolveCardImdb(req, en, ru);

            // Код из словаря принимаем, только если его несёт хоть одна раздача.
            //
            // Под одним названием и годом словарь знает НЕСКОЛЬКО вещей, а
            // обратный поиск отдаёт первую попавшуюся. У «The Odyssey» 2026
            // там три записи: tt41605854 «The Odyssey», tt43677301 «The Odyssey:
            // The Making of an Epic» и tt33764258 «Одиссея / The Odyssey» —
            // настоящий фильм. Словарь отдавал первую, и заслон объявлял чужими
            // ровно те раздачи, у которых стоит ПРАВИЛЬНЫЙ код: карточка теряла
            // TS и CAMRip, оставляя два релиза без кода. Симптом обманчив —
            // выглядит как «находит мало», а не как ошибка кода.
            //
            // Если код карточки не встречается в выдаче ни разу, разводить им
            // нечего: он не подтверждает и не опровергает ни одной записи.
            // Тогда честнее вывести код из самой выдачи — этим занят разбор ниже.
            if (!string.IsNullOrEmpty(cardImdb) && !AnyResultHasCode(results, r => r.info?.imdb, cardImdb))
                cardImdb = null;

            // Словарь ответить не смог — спрашиваем саму выдачу, но строго:
            // код берём у раздач, совпавших с карточкой по ОБОИМ названиям.
            //
            // Обычному голосованию (ниже) это доверить нельзя: оно сверяет одно
            // оригинальное название, а «The Odyssey: The Making of an Epic» —
            // фильм о съёмках — по правилу подзаголовка тоже совпадает, и его
            // трёх раздач хватило бы, чтобы перевесить две настоящих.
            // Требование обоих названий его отсекает: русского названия
            // карточки у него нет.
            if (string.IsNullOrEmpty(cardImdb) && !string.IsNullOrEmpty(en) && !string.IsNullOrEmpty(ru))
                cardImdb = CodeVotedByExactCard(results, r => r.info?.imdb, en, ru, req);

            // Словарь знает не всё — «FROM» в нём нет. Но код можно вывести
            // из самой выдачи: раздачи, совпавшие по ОРИГИНАЛЬНОМУ названию,
            // заведомо свои, и их код — это код карточки.
            //
            // Зачем он нужен: совпадение по русскому названию возвращает своё
            // (аниме «Атака титанов» иначе теряется — на трекерах оно
            // «Shingeki no Kyojin»), но вместе с ним тянет тёзок: в выдачу
            // «Наследников» лезло «Descendants: Wicked Wonderland», 6 раздач
            // из 51. Код их разводит: у чужой вещи он другой.
            if (string.IsNullOrEmpty(cardImdb) && !string.IsNullOrEmpty(en))
            {
                var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in results)
                {
                    string code = r.info?.imdb;
                    if (string.IsNullOrEmpty(code))
                        continue;

                    if (!Hits(r.info?.name, r.info?.originalname, en, req.TitleOriginal))
                        continue;

                    // Голосовать может только раздача, чей год подходит
                    // карточке. Иначе побеждает более многочисленный ТЁЗКА:
                    // у карточки «Дюна» 2021 раздач второй части (2024) вдвое
                    // больше, и они объявляют своим чужой код — после чего
                    // верные раздачи с настоящим кодом вылетают как «чужие».
                    // Замер 07.08.2026: выдача падала со 125 раздач до 22,
                    // и все 22 были записями вообще без кода.
                    if (!YearFits(r, req.Year, req.IsSerial))
                        continue;

                    votes.TryGetValue(code, out int n);
                    votes[code] = n + 1;
                }

                int best = 0;
                foreach (var pair in votes)
                {
                    if (pair.Value > best)
                    {
                        best = pair.Value;
                        cardImdb = pair.Key;
                    }
                }
            }

            // Код Кинопоиска — та же работа, но для русского кино, где кода
            // IMDB нет ни у одной раздачи. Карточку сводим к коду по паре
            // «название + год»; спрашиваем оба названия, потому что у русской
            // вещи оригинальное совпадает с русским.
            string cardKinopoisk = null;
            if (req.Year > 0)
            {
                if (!string.IsNullOrWhiteSpace(req.TitleOriginal))
                    JacBlack.Infrastructure.Persistence.KinopoiskIndex.TryGetByTitle(req.TitleOriginal, req.Year, out cardKinopoisk);

                if (string.IsNullOrEmpty(cardKinopoisk) && !string.IsNullOrWhiteSpace(req.Title))
                    JacBlack.Infrastructure.Persistence.KinopoiskIndex.TryGetByTitle(req.Title, req.Year, out cardKinopoisk);
            }

            // То же правило, что и для IMDB: код, которого нет ни у одной
            // раздачи, ничего не разводит, а вредить может.
            if (!string.IsNullOrEmpty(cardKinopoisk) && !AnyResultHasCode(results, r => r.info?.kinopoisk, cardKinopoisk))
                cardKinopoisk = null;

            // Словарь знает не всё, но код можно вывести из самой выдачи —
            // как и для IMDB. Голосуем по раздачам, совпавшим по названию:
            // они заведомо свои, и их код — код карточки.
            //
            // Для русского кино голосуем по ЛЮБОМУ из названий, а не только по
            // оригинальному: у него оба поля русские и совпадают, а требовать
            // «оригинальное» значило бы не голосовать вовсе.
            if (string.IsNullOrEmpty(cardKinopoisk))
            {
                var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in results)
                {
                    string code = r.info?.kinopoisk;
                    if (string.IsNullOrEmpty(code))
                        continue;

                    bool hit = (!string.IsNullOrEmpty(en) && Hits(r.info?.name, r.info?.originalname, en, req.TitleOriginal))
                        || (!string.IsNullOrEmpty(ru) && Hits(r.info?.name, r.info?.originalname, ru, req.Title));

                    if (!hit)
                        continue;

                    // Год — то же условие, что и для голосования по IMDB:
                    // без него побеждает более многочисленный тёзка соседних
                    // лет, и его код выкашивает верные раздачи.
                    if (!YearFits(r, req.Year, req.IsSerial))
                        continue;

                    votes.TryGetValue(code, out int n);
                    votes[code] = n + 1;
                }

                int best = 0;
                foreach (var pair in votes)
                {
                    if (pair.Value > best)
                    {
                        best = pair.Value;
                        cardKinopoisk = pair.Key;
                    }
                }
            }

            // Год добываем ДО отбора, а не смягчаем отбор.
            //
            // Ниже год работает условием: не совпал или не известен — раздача
            // выбывает. Правило нужное, но наказывало оно за наш собственный
            // разбор: сценовые релизы год в названии не пишут вовсе, и из
            // карточки «The Boys» вылетало 49 раздач piratebay из 51 — при том
            // что у двадцати из них стоял верный код IMDB.
            //
            // Здесь и место: коды карточки уже выведены, а отбор ещё не начался.
            ReleaseYearResolver.Fill(results, cardImdb, req.IsSerial);

            // Для сериалов заслон по коду смягчаем. Сезоны и эпизоды сериала
            // имеют СВОИ коды IMDB, отличные от кода сериала: у «The Boys»
            // сериал — tt1190634, а 3 сезон — tt22298582. Строгое «код не
            // совпал — чужое» выкидывало ровно такие раздачи (DV-релизы
            // 3 сезона несут код сезона), хотя это тот же сериал.
            //
            // Разделитель — подтверждённость. Настоящий сезон несёт свой код
            // на МНОГИХ раздачах, а мистег чужого фильма — на одной. Собираем
            // коды, встречающиеся у карточных раздач (совпали по названию,
            // типу и году) не меньше двух раз, — их считаем роднёй карточки и
            // по ним раздачу не отвергаем. У фильмов такого не бывает (у фильма
            // один код), поэтому для них правило остаётся строгим.
            var serialImdbFamily = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var serialKpFamily = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (req.IsSerial == 2)
            {
                CollectCorroboratedCodes(results, en, ru, req, r => r.info?.imdb, serialImdbFamily);
                CollectCorroboratedCodes(results, en, ru, req, r => r.info?.kinopoisk, serialKpFamily);
            }

            var kept = new List<Result>(results.Count);

            foreach (var r in results)
            {
                // Код есть у обоих и он разный — это заведомо чужая раздача,
                // сколько бы ни совпадали названия. Для сериала делаем
                // исключение подтверждённой семье кодов (сезоны/эпизоды).
                string imdb = r.info?.imdb;
                if (!string.IsNullOrEmpty(cardImdb) && !string.IsNullOrEmpty(imdb)
                    && !string.Equals(imdb, cardImdb, StringComparison.OrdinalIgnoreCase)
                    && !(req.IsSerial == 2 && serialImdbFamily.Contains(imdb)))
                    continue;

                // То же правило по коду Кинопоиска. Записи без кода не трогаем:
                // он известен у малой части базы, и строгость по нему выкосила
                // бы почти всё верное. Семье сезонов сериала — та же поблажка.
                string kinopoisk = r.info?.kinopoisk;
                if (!string.IsNullOrEmpty(cardKinopoisk) && !string.IsNullOrEmpty(kinopoisk)
                    && !string.Equals(kinopoisk, cardKinopoisk, StringComparison.OrdinalIgnoreCase)
                    && !(req.IsSerial == 2 && serialKpFamily.Contains(kinopoisk)))
                    continue;

                if (!TypeFits(r, req.IsSerial))
                    continue;

                // Код сильнее года.
                //
                // Год — признак слабый: карточка берёт его у TMDB, а трекеры
                // пишут свой, и они расходятся сплошь и рядом. У «Обсессии»
                // Лампа шлёт 2026, а все 44 раздачи в базе помечены 2025 —
                // строгий год оставлял ОДНУ раздачу из сорока четырёх, причём
                // у выброшенных стоял тот же код IMDB, что у выжившей. То есть
                // мы точно знали, что это тот же фильм, и всё равно теряли его.
                //
                // Код опознаёт вещь однозначно, поэтому: совпал код с карточкой —
                // год не спрашиваем вовсе. Лишнего это не пропустит: выше стоит
                // заслон «код есть у обоих и он разный — чужое», так что сюда
                // доходит либо совпадение, либо отсутствие кода. Год остаётся
                // условием ровно там, где подтвердить нечем.
                bool codeConfirms =
                    (!string.IsNullOrEmpty(cardImdb) && !string.IsNullOrEmpty(imdb)
                        && string.Equals(imdb, cardImdb, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(cardKinopoisk) && !string.IsNullOrEmpty(kinopoisk)
                        && string.Equals(kinopoisk, cardKinopoisk, StringComparison.OrdinalIgnoreCase));

                if (!codeConfirms && !YearFits(r, req.Year, req.IsSerial))
                    continue;

                string name = r.info?.name;
                string original = r.info?.originalname;

                // Раньше записи без разобранных названий пропускались: мол,
                // терять их хуже, чем пустить лишнее. Для поиска по карточке
                // это неверно — проверить их нечем, и именно они приносили
                // в выдачу «Извне» постороннее аниме. Раз клиент попросил
                // конкретную карточку, непроверяемому в ответе не место.
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(original))
                    continue;

                // Опознаёт раздачу ОРИГИНАЛЬНОЕ название. Русское как признак
                // негодно: у одной и той же вещи оно у каждого трекера своё —
                // «Атака титанов», «Нападение на Титан», «Вторжение титанов»;
                // а у карточки третье. Плюс разные вещи носят одинаковые
                // русские названия: «Извне» — это и сериал «From», и фильм
                // «From Beyond» 1986 года.
                //
                // Поэтому: прислали оригинальное — сверяем его и только его.
                // Не прислали — деваться некуда, сверяем русское.
                //
                // Замеры 01.08.2026, что давало прежнее правило «оба сразу»:
                // «Медвежонок» (The Bear) — ноль раздач вместо 51, потому что
                // на трекерах он «Медведь»; «Атака титанов» — 6 вместо
                // нескольких десятков.
                // Годится совпадение по любому из названий, но год при этом
                // обязателен и проверяется выше — именно он разводит тёзок.
                //
                // Почему нельзя опираться на одно оригинальное: у аниме на
                // трекерах оно японское в латинице. У «Attack on Titan» это
                // «Shingeki no Kyojin» — 21 раздача, ещё 10 под «Shingeki no
                // kyojin: Attack on Titan». С карточкой они не совпадут
                // никогда, а русское «Атака титанов» совпадает точно.
                //
                // Почему безопасно вернуть русское: прежний повод отказаться
                // от него — фильм «From Beyond» 1986 года, который по-русски
                // тоже «Извне», — закрыт годом: карточка сериала говорит 2022,
                // и восемьдесят шестой в окно не попадает.
                bool byOriginal = Hits(name, original, en, req.TitleOriginal);

                // Совпадение по русскому названию принимаем осторожнее: оно
                // возвращает своё, но и тёзок тоже. Пропускаем, только если
                // код IMDB подтверждает — либо его у раздачи нет вовсе и
                // опровергнуть нечем (у аниме кода обычно нет, и терять его
                // из-за этого нельзя).
                //
                // Кинопоиск здесь особенно к месту: совпадение по русскому
                // названию — ровно тот случай, где тёзки и лезут, а код IMDB
                // у русского кино отсутствует и подтвердить ничего не может.
                bool byRussian = Hits(name, original, ru, req.Title)
                    && (string.IsNullOrEmpty(cardImdb)
                        || string.IsNullOrEmpty(imdb)
                        || string.Equals(imdb, cardImdb, StringComparison.OrdinalIgnoreCase)
                        || (req.IsSerial == 2 && serialImdbFamily.Contains(imdb)))
                    && (string.IsNullOrEmpty(cardKinopoisk)
                        || string.IsNullOrEmpty(kinopoisk)
                        || string.Equals(kinopoisk, cardKinopoisk, StringComparison.OrdinalIgnoreCase)
                        || (req.IsSerial == 2 && serialKpFamily.Contains(kinopoisk)));

                // Правило «или» держится на годе: он и разводит тёзок. А в
                // режимах поиска БЕЗ года («Оригинал», «Русский», «Оба
                // названия») разводить нечем, и «или» пропускает всё подряд.
                // Замер на карточке «Одиссея / The Odyssey»: 33 раздачи, из
                // них «Одиссей / The Odyssey» 1997 года — 19 штук, плюс
                // «Одиссея / L'odyssee» 2016, «L'Odissea» 1911 и 1969.
                // Русское название не совпадало ни у первых, оригинальное —
                // ни у вторых, и каждому хватало половины совпадения.
                //
                // Поэтому: года нет и присланы ОБА названия — требуем оба.
                // Требуем, разумеется, только когда у самой раздачи оба
                // разобраны: у части источников есть лишь одно, и такую
                // запись проверить нечем.
                //
                // Строгость безопасна, потому что ниже стоит запасной ход: если
                // она выкосит карточку целиком (у сериала «The Bear» карточка
                // присылает «Медвежонок», а на трекерах он «Медведь»), выдача
                // пересобирается по одному оригинальному названию.
                bool ok;

                bool обаПрисланы = !string.IsNullOrEmpty(en) && !string.IsNullOrEmpty(ru);
                bool обаРазобраны = !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(original);

                if (req.Year <= 0 && обаПрисланы && обаРазобраны)
                    ok = byOriginal && byRussian;
                else
                    ok = byOriginal || byRussian;

                if (ok)
                    kept.Add(r);
            }

            // Строгое правило может выкосить карточку целиком, и это не
            // теория: у сериала «The Bear» карточка присылает русское название
            // «Медвежонок», а на трекерах он «Медведь» — 45 раздач, и все
            // отсеивались, выдача обнулялась.
            //
            // Поэтому если совпадение обоих названий не дало НИЧЕГО, опираемся
            // на оригинальное: переводы у карточки и у релизёров расходятся
            // сплошь и рядом, а оригинал один. Год при этом продолжает
            // работать и не пускает однофамильцев.
            if (kept.Count == 0 && !string.IsNullOrEmpty(en))
            {
                foreach (var r in results)
                {
                    string name = r.info?.name;
                    string original = r.info?.originalname;

                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(original))
                        continue;

                    if (!YearFits(r, req.Year, req.IsSerial))
                        continue;

                    if (Hits(name, original, en, req.TitleOriginal))
                        kept.Add(r);
                }
            }

            // Последний рубеж: карточка пуста, а раздачи с подходящим названием
            // есть — и отсеял их ТОЛЬКО неразобранный год.
            //
            // Так обнулялся «Медвежонок» в режиме «Русский + Год»: четыре
            // раздачи по названию находились, но год у них не разобран, а
            // прежний ход не помогает — оригинального названия в этом режиме
            // клиент не присылает.
            //
            // Правило остаётся прежним: год указан — он условие. Но пустая
            // выдача хуже приблизительной, поэтому когда СТРОГО не нашлось
            // ничего, отдаём совпавшие по названию записи без года. У тех,
            // где год разобран, он по-прежнему обязан совпасть.
            if (kept.Count == 0 && req.Year > 0)
            {
                foreach (var r in results)
                {
                    if ((r.info?.relased ?? 0) > 0)
                        continue;

                    string name = r.info?.name;
                    string original = r.info?.originalname;

                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(original))
                        continue;

                    if (!TypeFits(r, req.IsSerial))
                        continue;

                    if (Hits(name, original, en, req.TitleOriginal) || Hits(name, original, ru, req.Title))
                        kept.Add(r);
                }
            }

            return kept;
        }

        /// <summary>
        /// Год карточки с допуском.
        ///
        /// Зачем допуск, а не равенство. У сериала сезоны выходят годами позже
        /// первого: у «Извне» карточка говорит 2022, а четвёртый сезон — 2026,
        /// и по равенству мы выбросили бы почти всю выдачу. У фильма расхождение
        /// другое и куда меньше — дата премьеры и дата релиза могут разойтись
        /// на год в обе стороны.
        ///
        /// Зачем вообще: без года режим «Русский + Год» отдавал 38 чужих
        /// раздач — «From Beyond» 1986 года и «Alien Predator» 1987-го носят
        /// то же русское название «Извне», и одним названием они неотличимы,
        /// а годом разводятся сразу.
        ///
        /// Раздачи без года пропускаем: он разобран не у всех, и молчаливо
        /// терять их из-за этого нельзя.
        /// </summary>
        /// <summary>
        /// Отсев по типу: в карточке фильма сериалу не место.
        ///
        /// Зачем отдельно от года. Год — не заслон, когда его нет: раздача
        /// с неразобранным годом проходит намеренно, иначе потеряли бы много
        /// верного. А тип известен почти всегда, и он отвергает чужое даже
        /// при нулевом годе. Именно так в карточку фильма «Одиссея» 2026
        /// попадали сериалы 1968, 1992 и 1994 годов: год у них не разобрался,
        /// русское название совпало дословно, кода нет ни у карточки, ни
        /// у раздач — отвергнуть было нечем.
        ///
        /// Правило то же, что на пути из базы (<c>JackettCardMatcher</c>):
        /// фильму годятся movie, multfilm, documovie и anime. Аниме остаётся
        /// с обеих сторон намеренно — полнометражное аниме тоже фильм.
        ///
        /// Судим только карточку фильма. Для сериальной обратное правило
        /// опасно: одиночные серии и сборники на трекерах сплошь и рядом
        /// помечены как movie, и строгость выкосила бы верную выдачу.
        /// </summary>
        /// <summary>
        /// Код карточки по словарю, спрошенному ОБОИМИ названиями.
        ///
        /// Зачем так. Сам словарь достоверен — каждая запись настоящая.
        /// Ненадёжен ключ: обратный поиск устроен как «название + год → код»,
        /// а эта пара не уникальна, и одноимённые записи затирают друг друга.
        /// У «The Odyssey» 2026 их три: сам фильм (tt33764258, у него заполнены
        /// оба названия — «Одиссея» и «The Odyssey»), другая вещь того же года
        /// (tt41605854) и фильм о съёмках (tt43677301). По одному английскому
        /// названию словарь отдавал не ту, и заслон выбрасывал ровно те
        /// раздачи, у которых стоит ПРАВИЛЬНЫЙ код.
        ///
        /// Поэтому спрашиваем обоими названиями и берём тот код, чья запись
        /// сходится с карточкой по обоим. Если ответы расходятся и ни один
        /// не сходится по обоим — кода у карточки нет: лучше не разводить
        /// вовсе, чем развести неверно.
        /// </summary>
        static string ResolveCardImdb(IndexerSearchRequest req, string en, string ru)
        {
            // Словарь спрашиваем ТОЧНЫМ годом, без допуска.
            //
            // Допуск в год здесь пробовали 07.08.2026 и откатили: «Obsession» —
            // название частое, и по соседнему году словарь отдавал ЧУЖОЙ фильм.
            // Дальше заслон «код не совпал — чужое» объявлял чужими все верные
            // раздачи, и карточка отдавала ноль вместо сорока двух.
            //
            // Расхождение года карточки и года трекеров лечится не здесь, а
            // ниже: код выводится голосованием по самой выдаче, а раздача
            // с совпавшим кодом проходит независимо от года.
            string byEn = null, byRu = null;

            if (!string.IsNullOrWhiteSpace(req.TitleOriginal))
                JacBlack.Infrastructure.Persistence.ImdbIndex.TryGetByTitle(req.TitleOriginal, req.Year, out byEn);

            if (!string.IsNullOrWhiteSpace(req.Title))
                JacBlack.Infrastructure.Persistence.ImdbIndex.TryGetByTitle(req.Title, req.Year, out byRu);

            // Оба названия привели к одному коду — сомнений нет.
            if (!string.IsNullOrEmpty(byEn) && string.Equals(byEn, byRu, StringComparison.OrdinalIgnoreCase))
                return byEn;

            // Ответы разошлись: берём тот, чья запись в словаре сходится
            // с карточкой по обоим названиям.
            foreach (string candidate in new[] { byRu, byEn })
            {
                if (string.IsNullOrEmpty(candidate))
                    continue;

                if (!JacBlack.Infrastructure.Persistence.ImdbIndex.TryGet(candidate, out var entry) || entry == null)
                    continue;

                bool сошлосьПоОригиналу = string.IsNullOrEmpty(en) || Same(entry.OriginalName, en) || Same(entry.Name, en);
                bool сошлосьПоРусскому = string.IsNullOrEmpty(ru) || Same(entry.Name, ru) || Same(entry.OriginalName, ru);

                if (сошлосьПоОригиналу && сошлосьПоРусскому)
                    return candidate;
            }

            // Карточка пришла с одним названием — сверять не с чем, берём что есть.
            if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(ru))
                return byEn ?? byRu;

            return null;
        }

        /// <summary>
        /// Код, за который «голосуют» раздачи, совпавшие с карточкой по ОБОИМ
        /// названиям. Такое совпадение — сильнейшее свидетельство: оно отсекает
        /// и однофамильцев, и вещи-спутники вроде фильма о съёмках.
        /// Возвращает пусто, если таких раздач нет или у них нет кода.
        /// </summary>
        static string CodeVotedByExactCard(
            List<Result> results, Func<Result, string> pick, string en, string ru, IndexerSearchRequest req)
        {
            var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in results)
            {
                string code = pick(r);
                if (string.IsNullOrEmpty(code))
                    continue;

                string name = r.info?.name;
                string original = r.info?.originalname;

                if (!Hits(name, original, en, req.TitleOriginal) || !Hits(name, original, ru, req.Title))
                    continue;

                // Год обязателен и здесь: совпадение по обоим названиям ещё не
                // делает раздачу своей, если это продолжение соседнего года.
                if (!YearFits(r, req.Year, req.IsSerial))
                    continue;

                votes.TryGetValue(code, out int n);
                votes[code] = n + 1;
            }

            string best = null;
            int bestCount = 0;

            foreach (var pair in votes)
            {
                if (pair.Value > bestCount)
                {
                    bestCount = pair.Value;
                    best = pair.Key;
                }
            }

            return best;
        }

        /// <summary>
        /// Коды, встречающиеся не меньше двух раз у раздач, совпавших с
        /// карточкой по названию, типу и году, — «подтверждённая семья».
        /// Для сериала сюда попадают коды сезонов и эпизодов (у каждого много
        /// раздач); одиночный мистег чужого фильма — нет. Порог в две раздачи
        /// и есть заслон от тёзки: случайному коду в семью не пробиться.
        /// </summary>
        static void CollectCorroboratedCodes(
            List<Result> results, string en, string ru, IndexerSearchRequest req,
            Func<Result, string> pick, HashSet<string> family)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in results)
            {
                string code = pick(r);
                if (string.IsNullOrEmpty(code))
                    continue;

                if (!TypeFits(r, req.IsSerial))
                    continue;

                if (!YearFits(r, req.Year, req.IsSerial))
                    continue;

                bool hit = (!string.IsNullOrEmpty(en) && Hits(r.info?.name, r.info?.originalname, en, req.TitleOriginal))
                    || (!string.IsNullOrEmpty(ru) && Hits(r.info?.name, r.info?.originalname, ru, req.Title));

                if (!hit)
                    continue;

                counts.TryGetValue(code, out int n);
                counts[code] = n + 1;
            }

            foreach (var pair in counts)
                if (pair.Value >= 2)
                    family.Add(pair.Key);
        }

        /// <summary>Встречается ли код карточки хоть у одной раздачи в выдаче.</summary>
        static bool AnyResultHasCode(List<Result> results, Func<Result, string> pick, string code)
        {
            foreach (var r in results)
            {
                string value = pick(r);
                if (!string.IsNullOrEmpty(value) && string.Equals(value, code, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static bool TypeFits(Result r, int cardIsSerial)
        {
            if (cardIsSerial != 1)
                return true;

            var types = r.info?.types;
            if (types == null || types.Length == 0)
                return true;

            foreach (string t in types)
            {
                if (t == "movie" || t == "multfilm" || t == "documovie" || t == "anime")
                    return true;
            }

            return false;
        }

        static bool YearFits(Result r, int cardYear, int cardIsSerial)
        {
            if (cardYear <= 0)
                return true;

            int year = r.info?.relased ?? 0;

            // Год в запросе есть, а у раздачи не разобран — в карточку не
            // берём. Раньше пропускали: мол, «неизвестен» это не «не совпал»,
            // и терять записи из-за нашего же плохого разбора неправильно.
            // На деле именно так в карточку фильма «Одиссея» 2026 попадали
            // сериалы 1968, 1992 и 1994 годов — год у них не разобрался,
            // название совпало, и отвергнуть их было нечем.
            //
            // Раз человек указал год, он и есть условие отбора: непроверяемому
            // в такой выдаче не место. Путь из базы (JackettCardMatcher) так
            // работал всегда — там раздача без года в карточку с годом не
            // попадает вовсе; расходился только живой поиск.
            if (year <= 0)
                return false;

            // Карточка сама говорит, фильм это или сериал, и её слову верим
            // больше, чем типу раздачи. Иначе к карточке фильма «Дюна» 2021
            // приклеивался сериал-приквел «Дюна: Пророчество» 2024: у него
            // тип сериальный, значит допуск по году брался широкий, и год
            // его не отсекал. Замер 01.08.2026: 26 лишних раздач из 86.
            //
            // Год у фильма ТОЧНЫЙ, без допуска. Допуск ±1 стоял на случай
            // расхождения года премьеры и года производства, но платой за
            // него были чужие фильмы-тёзки соседних лет: в карточку «Одиссеи»
            // 2026 года лез «Odyssey» 2025-го. Раз год указан — он и есть
            // условие.
            if (cardIsSerial == 1)
                return year == cardYear;

            var types = r.info?.types;
            bool serial = cardIsSerial == 2;

            if (types != null)
            {
                foreach (string t in types)
                {
                    if (t == "serial" || t == "multserial" || t == "docuserial" || t == "tvshow" || t == "anime")
                    {
                        serial = true;
                        break;
                    }
                }
            }

            // Сериал: сезоны идут только вперёд, назад — лишь на год, на случай
            // если карточка датирована премьерой, а раздача пилотом.
            if (serial)
                return year >= cardYear - 1;

            // Фильм, тип которого карточка не назвала, — год всё равно точный.
            return year == cardYear;
        }

        static bool HasCyrillic(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;

            foreach (char c in s)
                if (c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я' || c == 'ё' || c == 'Ё')
                    return true;

            return false;
        }

        /// <summary>
        /// Название карточки сверяется РАВЕНСТВОМ, а не началом строки.
        ///
        /// Лампа присылает точное название: если написано «From», то это ровно
        /// «From», и додумывать нечего. Сравнение по началу пропускало чужое —
        /// «From Russia with Love» (1963) тоже начинается с «From» и лез
        /// в выдачу сериала «Извне».
        /// </summary>
        static bool Hits(string name, string original, string normalizedQuery, string rawQuery = null)
        {
            if (string.IsNullOrEmpty(normalizedQuery))
                return false;

            return SameOrSubtitle(name, normalizedQuery, rawQuery)
                || SameOrSubtitle(original, normalizedQuery, rawQuery);
        }

        static bool Same(string title, string normalizedQuery)
        {
            string t = JacBlack.Infrastructure.Utils.StringConvert.SearchName(title);
            return !string.IsNullOrEmpty(t) && string.Equals(t, normalizedQuery, StringComparison.Ordinal);
        }

        /// <summary>
        /// Совпадает ли название с карточкой с учётом подзаголовка.
        ///
        /// Строгое равенство слишком узко: карточка «Дюны» 2021 года приходит
        /// как «Dune», а раздачи называются «Dune: Part One», и все они
        /// выбрасывались — из 34 совпадений по названию в выдаче оставалось 3.
        ///
        /// Но продолжение принимаем только после двоеточия или тире. Пробел
        /// разрешать нельзя: «From Beyond» — это тоже «From» плюс слово, и по
        /// пробелу в выдачу сериала «Извне» вернулся бы чужой фильм.
        /// </summary>
        static bool SameOrSubtitle(string title, string normalizedQuery, string rawQuery)
        {
            if (Same(title, normalizedQuery))
                return true;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(rawQuery))
                return false;

            string t = title.Trim().ToLowerInvariant();
            string q = rawQuery.Trim().ToLowerInvariant();

            return t.StartsWith(q + ":", StringComparison.Ordinal)
                || t.StartsWith(q + " -", StringComparison.Ordinal)
                || t.StartsWith(q + " —", StringComparison.Ordinal);
        }
    }
}
