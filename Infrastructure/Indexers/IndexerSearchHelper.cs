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

            if (originalGiven || titleGiven)
                results = FilterByCardTitle(results, req, originalGiven, titleGiven);

            var (limit, offset) = IndexerRequestParams.LimitOffsetFromQuery(query);
            return IndexerResultFilters.Paginate(results, limit, offset);
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
        static List<Result> FilterByCardTitle(List<Result> results, IndexerSearchRequest req, bool originalGiven, bool titleGiven)
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
            if (req.Year > 0 && !string.IsNullOrWhiteSpace(req.TitleOriginal))
                JacBlack.Infrastructure.Persistence.ImdbIndex.TryGetByTitle(req.TitleOriginal, req.Year, out cardImdb);

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

            var kept = new List<Result>(results.Count);

            foreach (var r in results)
            {
                // Код есть у обоих и он разный — это заведомо чужая раздача,
                // сколько бы ни совпадали названия.
                string imdb = r.info?.imdb;
                if (!string.IsNullOrEmpty(cardImdb) && !string.IsNullOrEmpty(imdb)
                    && !string.Equals(imdb, cardImdb, StringComparison.OrdinalIgnoreCase))
                    continue;

                // То же правило по коду Кинопоиска. Записи без кода не трогаем:
                // он известен у малой части базы, и строгость по нему выкосила
                // бы почти всё верное.
                string kinopoisk = r.info?.kinopoisk;
                if (!string.IsNullOrEmpty(cardKinopoisk) && !string.IsNullOrEmpty(kinopoisk)
                    && !string.Equals(kinopoisk, cardKinopoisk, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TypeFits(r, req.IsSerial))
                    continue;

                if (!YearFits(r, req.Year, req.IsSerial))
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
                        || string.Equals(imdb, cardImdb, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrEmpty(cardKinopoisk)
                        || string.IsNullOrEmpty(kinopoisk)
                        || string.Equals(kinopoisk, cardKinopoisk, StringComparison.OrdinalIgnoreCase));

                bool ok = byOriginal || byRussian;

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
            if (year <= 0)
                return true;

            // Карточка сама говорит, фильм это или сериал, и её слову верим
            // больше, чем типу раздачи. Иначе к карточке фильма «Дюна» 2021
            // приклеивался сериал-приквел «Дюна: Пророчество» 2024: у него
            // тип сериальный, значит допуск по году брался широкий, и год
            // его не отсекал. Замер 01.08.2026: 26 лишних раздач из 86.
            if (cardIsSerial == 1)
                return year >= cardYear - 1 && year <= cardYear + 1;

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

            return year >= cardYear - 1 && year <= cardYear + 1;
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
