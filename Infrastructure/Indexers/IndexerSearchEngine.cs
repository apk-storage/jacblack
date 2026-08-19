using JacBlack.Application.Search;
using JacBlack.Infrastructure.Persistence;
using JacBlack.Infrastructure.Utils;
using JacBlack.Models.Api;
using JacBlack.Models.AppConf;
using JacBlack.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JacBlack.Infrastructure.Indexers
{
    public static class IndexerSearchEngine
    {
        public static async Task<List<Result>> SearchCombinedAsync(IndexerSearchRequest req, IMemoryCache cache, IJackettSearchService jackettSearch, ILiveSeeders liveSeeders = null)
        {
            var settings = IndexerSearchOptions.Resolve();
            string query = IndexerRequestParams.NormalizeQuery(req.Query);

            string titleRu = req.Title;
            string titleEn = req.TitleOriginal;
            if (string.IsNullOrWhiteSpace(titleRu) && string.IsNullOrWhiteSpace(titleEn))
            {
                var split = IndexerRequestParams.SplitBilingualQuery(query);
                titleRu = split.ru;
                titleEn = split.en;
            }

            bool imdbMode = !req.CardMode && IndexerRequestParams.IsImdbOrKpQuery(query);
            var batches = new List<IEnumerable<Result>>();

            if (imdbMode)
            {
                batches.Add(await V1SearchAsync(query, null, exact: true, settings.v1Sort, req.Trackers, req.Season, cache, req.RqNum));
                return await FinishAsync(batches, liveSeeders);
            }

            var category = BuildCategoryDict(req.Categories);
            int isSerial = ResolveIsSerial(req);

            if (req.CardMode)
            {
                var card = jackettSearch.SearchResults(req.ApiKey, query, titleRu, titleEn, req.Year, category, isSerial, req.RqNum, cache);
                batches.Add(card);

                // Карточка сериала приходит с номером сезона, а трекеры хранят
                // имя сериала без него. Ищем ВТОРЫМ заходом по имени без
                // номера — и делаем это независимо от того, нашлось ли что-то
                // первым: непустая выдача здесь не значит полную. По «Tsue to
                // Tsurugi no Wistoria S2» находились четыре раздачи одного
                // animetosho (он берёт имя из имени файла релиза), а nyaa,
                // aniliberty, animelayer, rutor и прочие выпадали — всего
                // 4 против 38.
                string bareQuery = IndexerRequestParams.StripTrailingSeason(query);
                string bareRu = IndexerRequestParams.StripTrailingSeason(titleRu);
                string bareEn = IndexerRequestParams.StripTrailingSeason(titleEn);
                if (bareQuery != null || bareRu != null || bareEn != null)
                {
                    batches.Add(jackettSearch.SearchResults(
                        req.ApiKey, bareQuery ?? query, bareRu ?? titleRu, bareEn ?? titleEn,
                        req.Year, category, isSerial, req.RqNum, cache));
                }

                if (card.Count == 0)
                {
                    foreach (var variant in BuildQueryVariants(query, titleRu, titleEn, settings))
                        batches.Add(jackettSearch.SearchResults(req.ApiKey, variant, null, null, 0, null, isSerial, false, cache));
                }

                // У аниме оригинальное название приходит иероглифами, а
                // англоязычные трекеры подписывают раздачи ромадзи — общего
                // в этих строках нет ни буквы. Ромадзи знают наши же русские
                // аниме-трекеры: у них в записи лежат оба имени.
                //
                // Ищем его по ВСЕМУ уже найденному, а не по первому заходу:
                // при японском названии первый заход часто пуст, и записи
                // приходят из запасных вариантов — именно там и лежит ромадзи.
                var aliases = PickAliases(batches, titleEn);
                if (aliases.Count > 1)
                {
                    req.TitleAliases = aliases;

                    // Второе и последующие написания ищем отдельно. У «Могилы
                    // светлячков» это «Hotaru no Haka» и «Grave of the
                    // Fireflies» — совершенно разные строки, и по одной из них
                    // половина раздач не находится.
                    foreach (string alias in aliases.Skip(1).Take(2))
                    {
                        batches.Add(jackettSearch.SearchResults(
                            req.ApiKey, alias, null, alias, 0, category, isSerial, req.RqNum, cache));
                    }
                }

                string romaji = aliases.FirstOrDefault();
                if (romaji != null)
                {
                    req.TitleRomaji = romaji;

                    // Год этому заходу не передаём. Поиск по базе выбрасывает
                    // раздачи с неразобранным годом, а у nyaa он нулевой всегда:
                    // года в имени релиза нет и взять его неоткуда. С годом
                    // заход возвращал одни русские трекеры, ради которых он и
                    // не затевался. Отбор по году сделает заслон — он умеет
                    // пропускать записи без года для сериальных карточек.
                    batches.Add(jackettSearch.SearchResults(
                        req.ApiKey, romaji, null, romaji, 0, category, isSerial, req.RqNum, cache));
                }
            }
            else
            {
                foreach (var variant in BuildQueryVariants(query, titleRu, titleEn, settings))
                    batches.Add(jackettSearch.SearchResults(req.ApiKey, variant, null, null, 0, null, isSerial, false, cache));
            }

            foreach (var pair in V1Pairs(query, titleRu, titleEn, settings, req.CardMode))
                batches.Add(await V1SearchAsync(pair.search, pair.altname, exact: false, settings.v1Sort, req.Trackers, req.Season, cache, req.RqNum));

            return await FinishAsync(batches, liveSeeders);
        }

        /// <summary>
        /// Общая доводка выдачи индексаторов: схлопнуть повторы и подменить сиды
        /// на живые. Раньше повторы убирались только в родном API, а живые сиды
        /// не доходили до пути Prowlarr — правки разъезжались по трём выдачам.
        /// </summary>
        static async Task<List<Result>> FinishAsync(List<IEnumerable<Result>> batches, ILiveSeeders liveSeeders)
        {
            var merged = IndexerResultMerger.MergeAndSort(batches.ToArray());
            merged = DuplicateFilter.RemoveSameTrackerDuplicates(merged);

            if (liveSeeders != null)
                merged = await liveSeeders.ApplyAsync(merged);

            return merged;
        }

        static Dictionary<string, string> BuildCategoryDict(List<int> categories)
        {
            if (categories == null || categories.Count == 0) return null;
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < categories.Count; i++)
                dict[$"Category[{i}]"] = categories[i].ToString();
            return dict;
        }

        static int ResolveIsSerial(IndexerSearchRequest req)
        {
            if (req.IsSerial >= 0) return req.IsSerial;
            return req.IsSerial;
        }

        /// <summary>
        /// Латинское имя, которым та же вещь подписана у англоязычных трекеров.
        ///
        /// Берётся из уже найденного: у русских аниме-трекеров в записи лежат
        /// оба имени сразу — «Адский режим» и «Hell Mode: Yarikomizuki no
        /// Gamer…». Возвращает null, если карточка и так пришла с латинским
        /// названием или подтвердить имя нечем.
        ///
        /// Порог в две записи — заслон от случайности: одна раздача с чужим
        /// именем в поле не должна утащить поиск в сторону.
        /// </summary>
        static List<string> PickAliases(List<IEnumerable<Result>> batches, string titleEn)
        {
            var пусто = new List<string>();
            if (batches == null || batches.Count == 0)
                return пусто;

            var found = new List<Result>();
            foreach (var batch in batches)
            {
                if (batch == null)
                    continue;
                found.AddRange(batch);
            }

            if (found.Count == 0)
                return пусто;

            // Карточка пришла с латинским названием — искать нечего.
            if (!string.IsNullOrWhiteSpace(titleEn) && !HasNonLatin(titleEn))
                return пусто;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var samples = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in found)
            {
                string original = r.info?.originalname;
                if (string.IsNullOrWhiteSpace(original) || HasNonLatin(original))
                    continue;

                string key = StringConvert.SearchName(original);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
                samples[key] = original;
            }

            // Берём ВСЕ написания, подтверждённые хотя бы двумя записями, самые
            // частые первыми. Одного мало: у «Могилы светлячков» релизы
            // подписаны и «Hotaru no Haka», и «Grave of the Fireflies» —
            // это разные строки, и по одной из них половина раздач не
            // находится. Порог в две записи отсекает случайный мусор в поле.
            var результат = new List<string>();
            foreach (var pair in counts.OrderByDescending(p => p.Value))
            {
                if (pair.Value < 2)
                    break;

                string имя = BaseTitle(samples[pair.Key]);
                if (!string.IsNullOrWhiteSpace(имя)
                    && !результат.Any(x => string.Equals(x, имя, StringComparison.OrdinalIgnoreCase)))
                {
                    результат.Add(имя);
                }

                if (результат.Count >= 3)      // больше трёх заходов не окупается
                    break;
            }

            return результат;
        }

        /// <summary>
        /// Имя до подзаголовка: «Hell Mode: Yarikomizuki no Gamer…» → «Hell Mode».
        ///
        /// Русские аниме-трекеры пишут полное название с подзаголовком, а
        /// англоязычные сокращают до основного: у nyaa те же раздачи подписаны
        /// «Hell Mode S2» и «Hell Mode: Yarikomizuki…». Полным именем находились
        /// три раздачи, базовым — сорок три, из них десять nyaa.
        ///
        /// Совсем короткие основы («Re», «One») не берём: по ним нашлось бы
        /// пол-архива, а отсеивать это потом нечем.
        /// </summary>
        static string BaseTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            int cut = title.IndexOf(':');
            if (cut < 0)
                return title;

            string head = title.Substring(0, cut).Trim();
            return head.Length >= 4 ? head : title;
        }

        static bool HasNonLatin(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;

            foreach (char c in s)
            {
                // Кириллица, японская кана и иероглифы — всё, что не подписывают
                // латиницей англоязычные трекеры.
                if (c >= 0x0400 && c <= 0x04FF) return true;
                if (c >= 0x3040 && c <= 0x30FF) return true;
                if (c >= 0x4E00 && c <= 0x9FFF) return true;
            }

            return false;
        }

        static List<string> BuildQueryVariants(string query, string titleRu, string titleEn, SearchSettings settings)
        {
            var variants = new List<string>();
            bool skipCombined = !string.IsNullOrWhiteSpace(query) && query.Contains(" / ") && (!string.IsNullOrWhiteSpace(titleRu) || !string.IsNullOrWhiteSpace(titleEn));

            if (!string.IsNullOrWhiteSpace(query) && !skipCombined)
            {
                if (settings.stripTrailingYear)
                {
                    var stripped = IndexerRequestParams.StripTrailingYear(query);
                    if (!string.IsNullOrWhiteSpace(stripped)) variants.Add(stripped);
                }
                if (!variants.Contains(query)) variants.Add(query);
            }

            foreach (var term in new[] { titleRu, titleEn })
            {
                if (!string.IsNullOrWhiteSpace(term) && !variants.Contains(term))
                    variants.Add(term);
            }

            // Название без номера сезона. Лампа шлёт карточку как «Tsue to
            // Tsurugi no Wistoria S2», а трекеры хранят имя сериала без номера,
            // и поиск по индексу такую строку просто не находил: из всей
            // выдачи уцелевал animetosho, который берёт имя из имени файла
            // релиза. Замер на живой базе: 4 раздачи с номером против 38 без.
            //
            // Пробуем дополнительным вариантом, а не заменой: раздачи, где
            // номер сезона в имени есть, по-прежнему находятся первым запросом.
            foreach (var term in new[] { query, titleRu, titleEn })
            {
                var bare = IndexerRequestParams.StripTrailingSeason(term);
                if (!string.IsNullOrWhiteSpace(bare) && !variants.Contains(bare))
                    variants.Add(bare);
            }

            if (variants.Count == 0 && !string.IsNullOrWhiteSpace(query))
                variants.Add(query);

            return variants;
        }

        static List<(string search, string altname)> V1Pairs(string query, string titleRu, string titleEn, SearchSettings settings, bool cardMode)
        {
            var mode = (settings.mergeV1 ?? "auto").ToLowerInvariant();
            if (mode == "false" || mode == "0") return new List<(string, string)>();

            // auto: v1 fuzzy только для fuzzy mode, не для card (Lampa card search)
            if (cardMode && mode == "auto")
                return new List<(string, string)>();

            if (mode == "true" || mode == "1")
                return V1SearchPairs(query, titleRu, titleEn, settings, null);

            return V1SearchPairs(query, titleRu, titleEn, settings, Math.Max(1, settings.maxV1Pairs));
        }

        static List<(string search, string altname)> V1SearchPairs(string query, string titleRu, string titleEn, SearchSettings settings, int? maxPairs)
        {
            var pairs = new List<(string, string)>();
            var seen = new HashSet<string>();

            void add(string search, string altname = null)
            {
                if (string.IsNullOrWhiteSpace(search)) return;
                string key = search + "\0" + (altname ?? "");
                if (!seen.Add(key)) return;
                pairs.Add((search, altname));
            }

            if (!string.IsNullOrWhiteSpace(titleRu) && !string.IsNullOrWhiteSpace(titleEn))
            {
                add(titleEn, titleRu);
                add(titleRu, titleEn);
            }
            else if (!string.IsNullOrWhiteSpace(titleRu))
                add(titleRu, titleEn);
            else if (!string.IsNullOrWhiteSpace(titleEn))
                add(titleEn, titleRu);

            foreach (var term in BuildQueryVariants(query, titleRu, titleEn, settings))
            {
                add(term);
                if (!string.IsNullOrWhiteSpace(titleRu) && !term.Contains(titleRu)) add(term, titleRu);
                if (!string.IsNullOrWhiteSpace(titleEn) && !term.Contains(titleEn)) add(term, titleEn);
            }

            if (maxPairs.HasValue && maxPairs.Value > 0 && pairs.Count > maxPairs.Value)
                return pairs.Take(maxPairs.Value).ToList();
            return pairs;
        }

        static async Task<List<Result>> V1SearchAsync(string search, string altname, bool exact, string sort, List<string> trackers, int? season, IMemoryCache cache, bool rqnum)
        {
            if (string.IsNullOrWhiteSpace(search)) return new List<Result>();

            (search, altname) = await ResolveImdbSearchAsync(search, altname, cache);

            var torrents = new Dictionary<string, TorrentDetails>();
            void add(TorrentDetails t)
            {
                if (AppInit.conf.synctrackers != null && !AppInit.conf.synctrackers.Contains(t.trackerName)) return;
                if (AppInit.conf.disable_trackers != null && AppInit.conf.disable_trackers.Contains(t.trackerName)) return;
                if (!MatchesTrackerFilter(t.trackerName, trackers)) return;
                if (!torrents.TryGetValue(t.url, out var val) || t.updateTime > val.updateTime)
                    torrents[t.url] = t;
            }

            string sn = StringConvert.SearchName(search);
            string altSn = StringConvert.SearchName(altname);

            if (string.IsNullOrEmpty(sn) && string.IsNullOrEmpty(altSn))
                return new List<Result>();

            if (exact)
            {
                foreach (var mdb in FileDB.masterDb.Where(i => (sn != null && (i.Key.StartsWith($"{sn}:") || i.Key.EndsWith($":{sn}"))) || (altSn != null && i.Key.Contains(altSn))))
                {
                    foreach (var t in FileDB.OpenRead(mdb.Key, true).Values)
                    {
                        if (t.types == null) continue;
                        string n = t._sn ?? StringConvert.SearchName(t.name);
                        string o = t._so ?? StringConvert.SearchName(t.originalname);
                        if (n == sn || o == sn || (altSn != null && (n == altSn || o == altSn)))
                            add(t);
                    }
                }
            }
            else
            {
                var mdb = FileDB.masterDb.Where(i => (sn != null && i.Key.Contains(sn)) || (altSn != null && i.Key.Contains(altSn)));
                if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
                    mdb = mdb.Take(AppInit.conf.maxreadfile);
                foreach (var val in mdb)
                {
                    foreach (var t in FileDB.OpenRead(val.Key, true).Values)
                    {
                        if (t.types != null) add(t);
                    }
                }
            }

            IEnumerable<TorrentDetails> query = torrents.Values;
            switch (sort ?? "sid")
            {
                case "pir": query = query.OrderByDescending(i => i.pir); break;
                case "size": query = query.OrderByDescending(i => i.size); break;
                default: query = query.OrderByDescending(i => i.sid); break;
            }

            if (season.HasValue && season.Value > 0)
                query = query.Where(i => i.seasons != null && i.seasons.Contains(season.Value));

            return query.Take(2000).Select(i => MapV1(i, rqnum)).ToList();
        }

        static bool MatchesTrackerFilter(string trackerName, List<string> trackers)
        {
            if (trackers == null || trackers.Count == 0)
                return true;
            if (string.IsNullOrWhiteSpace(trackerName))
                return false;

            foreach (var part in trackerName.Split(','))
            {
                foreach (var allowed in trackers)
                {
                    if (part.Trim().Equals(allowed, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        static Task<(string search, string altname)> ResolveImdbSearchAsync(string search, string altname, IMemoryCache cache)
            => Application.Search.TitleResolver.ResolveAsync(search, altname, cache);

        /// <summary>
        /// Тип раздачи в категории Newznab. Раньше сюда попадали только
        /// movie, serial и anime, а documovie, docuserial, tvshow, multfilm,
        /// multserial и sport уходили вообще без категории.
        ///
        /// Без категории запись проходит ЛЮБОЙ фильтр (см. FilterByCategory),
        /// то есть спорт попадал и в поиск по фильмам. Раскладка сделана так,
        /// чтобы запись оставалась в том разделе, где её ждут: документальный
        /// фильм — фильм, мультсериал — сериал.
        /// </summary>
        internal static readonly Dictionary<string, (int[] cats, string desc)> TypeToCategory = new()
        {
            ["movie"] = (new[] { 2000 }, "Movies"),
            ["documovie"] = (new[] { 2000 }, "Movies"),
            ["multfilm"] = (new[] { 2000 }, "Movies"),
            ["serial"] = (new[] { 5000 }, "TV"),
            ["multserial"] = (new[] { 5000 }, "TV"),
            ["tvshow"] = (new[] { 5000 }, "TV"),
            ["docuserial"] = (new[] { 5000, 5080 }, "TV/Documentary"),
            ["anime"] = (new[] { 5070 }, "TV/Anime"),
            ["sport"] = (new[] { 5060 }, "TV/Sport")
        };

        static Result MapV1(TorrentDetails i, bool rqnum)
        {
            var cats = new HashSet<int>();
            string catDesc = null;
            if (i.types != null)
            {
                foreach (var type in i.types)
                {
                    if (type == null || !TypeToCategory.TryGetValue(type, out var mapped))
                        continue;

                    foreach (int c in mapped.cats)
                        cats.Add(c);

                    // У раздачи может быть несколько типов; для подписи берём
                    // первый распознанный, чтобы результат не зависел от порядка.
                    catDesc ??= mapped.desc;
                }
            }

            return new Result
            {
                Tracker = i.trackerName,
                Details = i.url != null && i.url.StartsWith("http") ? Application.Search.TrackerUrlHygiene.Canonical(i.url) : null,
                // Лампа ищет DV буквальными словами, поэтому сокращённой подписи
                // («DV Profile 10.1») дописываем полное написание.
                Title = Parsing.DolbyVisionTag.Normalize(i.title),
                Size = i.size,
                PublishDate = i.createTime,
                Category = cats,
                CategoryDesc = catDesc,
                Seeders = i.sid,
                Peers = i.pir,
                MagnetUri = Application.Search.MagnetHygiene.Clean(i.magnet),
                ffprobe = rqnum || !AppInit.conf.tracks ? null : i.ffprobe,
                languages = i.languages,
                info = rqnum ? null : new TorrentInfo
                {
                    name = i.name,
                    originalname = i.originalname,
                    relased = i.relased,
                    // Dolby Vision выводим из названия: videotype знает только
                    // sdr и hdr, и DV-раздача в нём неотличима от обычной.
                    dv = Parsing.DolbyVisionTag.Value(i.title),
                    sizeName = i.sizeName,
                    // Качество и видеотип живой поиск не переносил вовсе, и
                    // раздачи приходили с quality=0. Пока путь из базы отдавал
                    // почти всё, это было незаметно; стоило карточке пойти
                    // живым поиском («Обсессия»: год карточки 2026, у раздач
                    // 2025), и из 42 раздач 41 оказалась без качества — фильтр
                    // 4К не находил ничего.
                    quality = i.quality,
                    videotype = i.videotype,
                    voices = i.voices,
                    seasons = i.seasons,
                    types = i.types,
                    media = MediaSummary(i.ffprobe, i.title),
                    imdb = i.imdb,
                    // Без этой строки код Кинопоиска в выдачу не попадал вовсе,
                    // и весь отбор по нему — заслон карточки, подстановка года —
                    // работал вхолостую: поле всегда было пустым. В базе он при
                    // этом есть, 10 260 записей проставлены миграцией.
                    kinopoisk = i.kinopoisk,
                    seedersLive = SeedersFreshness.IsFresh(i.updateTime),
                    seedersUnknown = SeedersFreshness.TrackerHidesSeeders(i.trackerName)
                }
            };
        }

        /// <summary>
        /// Сводку дорожек собираем и здесь: выдача Torznab и выдача Jackett
        /// строятся разными путями, и оставить её в одном значило бы завести
        /// расхождение — ровно то, на чём мы уже обжигались с размером.
        /// Пустую сводку не отдаём.
        /// </summary>
        static Parsing.MediaTracks.Summary MediaSummary(System.Collections.Generic.List<JacBlack.Models.Tracks.ffStream> ffprobe, string title)
        {
            var summary = Parsing.MediaTracks.Build(ffprobe, title);
            return summary.IsEmpty ? null : summary;
        }
    }
}
