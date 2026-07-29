using JacRed.Application.Search;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Api;
using JacRed.Models.AppConf;
using JacRed.Models.Details;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JacRed.Infrastructure.Indexers
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
                if (card.Count == 0)
                {
                    foreach (var variant in BuildQueryVariants(query, titleRu, titleEn, settings))
                        batches.Add(jackettSearch.SearchResults(req.ApiKey, variant, null, null, 0, null, isSerial, false, cache));
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
                Title = i.title,
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
                    sizeName = i.sizeName,
                    voices = i.voices,
                    seasons = i.seasons,
                    types = i.types
                }
            };
        }
    }
}
