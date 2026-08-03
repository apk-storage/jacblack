using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Tracks;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;
using JacRed.Models;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Application.Search
{
    public class TorrentQueryService : ITorrentQueryService
    {
        readonly ILiveSeeders _liveSeeders;
        readonly JacRed.Application.Index.IFastDbIndex _fastDbIndex;
        readonly ClosedTrackerSeeders _closedTrackers;

        public TorrentQueryService(
            ILiveSeeders liveSeeders,
            JacRed.Application.Index.IFastDbIndex fastDbIndex,
            Infrastructure.Trackers.Kinozal.KinozalSyncService kinozal = null,
            Infrastructure.Trackers.Toloka.TolokaSyncService toloka = null,
            Infrastructure.Trackers.Bitru.BitruApiSyncService bitru = null)
        {
            _liveSeeders = liveSeeders;
            _fastDbIndex = fastDbIndex;
            _closedTrackers = new ClosedTrackerSeeders(kinozal, toloka, bitru);
        }

        /// <summary>
        /// Ключи базы, у которых одна из половин точно равна искомому имени.
        ///
        /// Индекс FastDb хранит обе половины ключа отдельно, поэтому здесь
        /// достаточно двух обращений по словарю вместо обхода всех ключей.
        /// Если индекс почему-то пуст — откатываемся на обход, но уже с
        /// побайтовым сравнением и без лишних строк на каждый ключ.
        /// </summary>
        IEnumerable<string> ExactKeys(string search, string altsearch)
        {
            var index = _fastDbIndex?.Get();

            if (index != null && index.Count > 0)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (string name in new[] { search, altsearch })
                {
                    if (string.IsNullOrEmpty(name) || !index.TryGetValue(name, out var keys))
                        continue;

                    foreach (string key in keys)
                    {
                        if (seen.Add(key))
                            yield return key;
                    }
                }

                yield break;
            }

            string prefix = search == null ? null : search + ":";
            string suffix = search == null ? null : ":" + search;

            foreach (var item in FileDB.masterDb)
            {
                bool hit =
                    (prefix != null && (item.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                                        item.Key.EndsWith(suffix, StringComparison.Ordinal))) ||
                    (altsearch != null && item.Key.Contains(altsearch, StringComparison.Ordinal));

                if (hit)
                    yield return item.Key;
            }
        }

        public async Task<object> QueryTorrentsAsync(string search, string altname, bool exact, string type, string sort, string tracker, string voice, string videotype, long relased, long quality, long season, IMemoryCache memoryCache)
        {
            #region search kp/imdb
            (search, altname) = await TitleResolver.ResolveAsync(search, altname, memoryCache);
            #endregion

            // Если второе название не прислали, подставляем его из словаря.
            // Индекс ищет подстроку по склейке «имя : оригинальное имя»: у
            // русских трекеров там оба языка, а у yts, eztv и piratebay оба
            // поля английские — запрос «Веном» с ними не совпадёт физически.
            // Замер 31.07.2026: «Веном» — 196 раздач и ни одной от yts,
            // «Venom» — 213, из них 35 от yts.
            if (string.IsNullOrWhiteSpace(altname))
            {
                var counterparts = ImdbIndex.Counterparts(search);
                if (counterparts.Count > 0)
                    altname = counterparts[0];
            }

            #region Выборка
            var torrents = new Dictionary<string, TorrentDetails>();

            #region AddTorrents
            void AddTorrents(TorrentDetails t)
            {
                if (AppInit.conf.synctrackers != null && !AppInit.conf.synctrackers.Contains(t.trackerName))
                    return;

                if (AppInit.conf.disable_trackers != null && AppInit.conf.disable_trackers.Contains(t.trackerName))
                    return;

                if (torrents.TryGetValue(t.url, out TorrentDetails val))
                {
                    if (t.updateTime > val.updateTime)
                        torrents[t.url] = t;
                }
                else
                {
                    torrents.TryAdd(t.url, t);
                }
            }
            #endregion

            if (string.IsNullOrWhiteSpace(search) || search.Length == 1)
                return (torrents);

            string _s = StringConvert.SearchName(search);
            string _altsearch = StringConvert.SearchName(altname);

            if (string.IsNullOrEmpty(_s) && string.IsNullOrEmpty(_altsearch))
                return (torrents);

            if (exact)
            {
                #region Точный поиск
                // Ключ базы это «имя:оригинальноеимя», и индекс FastDb как раз
                // хранит обе половины отдельно. Раньше здесь шёл перебор всех
                // 296 тысяч ключей, причём внутри проверки на КАЖДЫЙ ключ
                // строились две новые строки ($"{_s}:") и сравнение шло с учётом
                // культуры. Замер 29.07.2026: пустой точный запрос занимал
                // 1401 мс против 184 мс у нечёткого. Теперь это выборка по
                // индексу — сразу нужные ключи, без обхода.
                foreach (string key in ExactKeys(_s, _altsearch))
                {
                    foreach (var t in FileDB.OpenRead(key, true).Values)
                    {
                        if (t.types == null)
                            continue;

                        if (string.IsNullOrWhiteSpace(type) || t.types.Contains(type))
                        {
                            string _n = t._sn ?? StringConvert.SearchName(t.name);
                            string _o = t._so ?? StringConvert.SearchName(t.originalname);

                            if (_n == _s || _o == _s || (_altsearch != null && (_n == _altsearch || _o == _altsearch)))
                                AddTorrents(t);
                        }
                    }

                }
                #endregion
            }
            else
            {
                #region Поиск по совпадению ключа в имени
                // Здесь обход неизбежен: ищем подстроку, а индекс хранит целые
                // половины ключа. Но сравнение — побайтовое: по умолчанию
                // Contains у строк учитывает культуру и работает заметно дольше.
                var mdb = FileDB.masterDb.Where(i =>
                    (_s != null && i.Key.Contains(_s, StringComparison.Ordinal)) ||
                    (_altsearch != null && i.Key.Contains(_altsearch, StringComparison.Ordinal)));
                if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
                    mdb = mdb.Take(AppInit.conf.maxreadfile);

                foreach (var val in mdb)
                {
                    foreach (var t in FileDB.OpenRead(val.Key, true).Values)
                    {
                        if (t.types == null)
                            continue;

                        if (!string.IsNullOrWhiteSpace(type) && !t.types.Contains(type))
                            continue;

                        // Ключ шарда отобрал кандидатов подстрокой, но пробелов
                        // в нём нет — потому «Веном» и попадал внутрь
                        // «Стиве|ном|Хокингом». Здесь исходные названия под
                        // рукой, и совпадение проверяется по границе слова.
                        if (!TitleMatch.Matches(t.name, t.originalname, _s, _altsearch))
                            continue;

                        AddTorrents(t);
                    }

                }
                #endregion
            }

            if (torrents.Count == 0)
                return (torrents);

            // Одна и та же тема трекера может лежать под двумя доменами —
            // адрес это ключ записи, а домены меняются. Для человека это
            // просто повтор в списке.
            var deduped = DuplicateFilter.RemoveSameTrackerDuplicates(torrents.Values, t => t);

            // Сиды из базы могут быть годовалой давности. Опрашиваем трекеры
            // и подменяем на живые — раньше это делалось только для выдачи
            // индексаторов, а Лампа ходит сюда.
            if (_liveSeeders != null)
                deduped = await _liveSeeders.ApplyAsync(deduped);

            // Закрытые трекеры анонсом не опрашиваются, у них свои пути —
            // общий слой знает какие. До 02.08.2026 он висел только на выдаче
            // индексаторов, и сайт из-за этого показывал числа из базы:
            // удалённая раздача «Кода 8» стояла у него первой строкой с 96
            // раздающими, когда в Лампе она уже была исправлена.
            var targets = deduped
                .Select(t => new SeedTarget
                {
                    Key = t.url,
                    Tracker = t.trackerName,
                    Urls = t.url,
                    Apply = (sid, pir) => { t.sid = sid; t.pir = pir; }
                })
                .ToList();

            // Оригинальное название берём из самих найденных записей, а не
            // только из словаря. Сайт шлёт одну строку запроса, и словарь на
            // неё отвечает не всегда — из-за этого на сайте проверенными были
            // 39% раздач против 89–97% в Лампе, которая присылает оба названия.
            // А в выдаче оригинал лежит прямо в записях: берём самый частый.
            string dominantOriginal = altname;
            if (string.IsNullOrWhiteSpace(dominantOriginal))
            {
                dominantOriginal = deduped
                    .Where(t => !string.IsNullOrWhiteSpace(t.originalname))
                    .GroupBy(t => t.originalname, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault();
            }

            var verified = await _closedTrackers.ApplyAsync(targets, dominantOriginal, search);

            // Раздачи, которых на трекере уже нет, не показываем: скачать их
            // нельзя, а число сидов у них — прошлогодний снимок.
            IEnumerable<TorrentDetails> query = deduped
                .Where(t => !ClosedTrackerSeeders.IsDead(t.trackerName, t.url));

            #region sort
            switch (sort ?? string.Empty)
            {
                case "sid":
                    // Проверенные числа выше непроверенных: непроверенное —
                    // снимок неизвестной давности, и прошлогодние 96 раздающих
                    // не должны стоять над сегодняшними 44.
                    query = query
                        .OrderByDescending(i => verified.Contains(i.url))
                        .ThenByDescending(i => i.sid);
                    break;
                case "pir":
                    query = query.OrderByDescending(i => i.pir);
                    break;
                case "size":
                    query = query.OrderByDescending(i => i.size);
                    break;
                case "create":
                    query = query.OrderByDescending(i => i.createTime);
                    break;
                case "update":
                    query = query.OrderByDescending(i => i.updateTime);
                    break;
            }
            #endregion

            if (!string.IsNullOrWhiteSpace(tracker))
                query = query.Where(i => i.trackerName == tracker);

            if (relased > 0)
                query = query.Where(i => i.relased == relased);

            if (quality > 0)
                query = query.Where(i => i.quality == quality);

            if (!string.IsNullOrWhiteSpace(videotype))
                query = query.Where(i => i.videotype == videotype);

            if (!string.IsNullOrWhiteSpace(voice))
                query = query.Where(i => i.voices.Contains(voice));

            if (season > 0)
                query = query.Where(i => i.seasons.Contains((int)season));
            #endregion

            return (query.Take(2_000).Select(i => new
            {
                tracker = i.trackerName,
                url = i.url != null && i.url.StartsWith("http") ? TrackerUrlHygiene.Canonical(i.url) : null,
                i.title,
                i.size,
                i.sizeName,
                i.createTime,
                i.updateTime,
                i.sid,
                i.pir,
                magnet = MagnetHygiene.Clean(i.magnet),
                i.name,
                i.originalname,
                i.relased,
                i.videotype,
                i.quality,
                i.voices,
                i.seasons,
                i.types,
                // Сводку дорожек считаем здесь, а не на стороне интерфейса:
                // разбор кодека из заголовка — та же логика, что в выдаче
                // Jackett, и вторая её копия на TypeScript неминуемо разошлась
                // бы с этой. Расхождения между копиями одной логики у нас уже
                // оборачивались потерянными записями.
                media = MediaSummary(i.ffprobe, i.title),
                i.imdb,
                // Проверено ли число раздающих прямо сейчас — живым опросом
                // либо свежим обходом. Ложь означает снимок из базы, который
                // может быть сделан хоть полгода назад.
                seedersLive = verified.Contains(i.url)
                    || Infrastructure.Indexers.SeedersFreshness.IsFresh(i.updateTime),
                // Трекер не сообщает числа вовсе: у lostfilm счётчиков нет,
                // и единица в записи проставлена разбором, а не данными.
                seedersUnknown = Infrastructure.Indexers.SeedersFreshness.TrackerHidesSeeders(i.trackerName)
            }));
        }
        /// <summary>Пустую сводку не отдаём: пустые плашки на карточке лишние.</summary>
        static Infrastructure.Parsing.MediaTracks.Summary MediaSummary(System.Collections.Generic.List<JacRed.Models.Tracks.ffStream> ffprobe, string title)
        {
            var summary = Infrastructure.Parsing.MediaTracks.Build(ffprobe, title);
            return summary.IsEmpty ? null : summary;
        }

        public object QueryQualitys(string name, string originalname, string type, int page = 1, int take = 1000)
        {
            string _s = StringConvert.SearchName(name);
            string _so = StringConvert.SearchName(originalname);

            if (string.IsNullOrEmpty(_s) && string.IsNullOrEmpty(_so))
                return (new Dictionary<string, Dictionary<int, Models.TorrentQuality>>());

            var torrents = new Dictionary<string, Dictionary<int, Models.TorrentQuality>>();

            #region AddTorrents
            void AddTorrents(TorrentDetails t)
            {
                if (t?.types == null || t.types.Contains("sport") || t.relased == 0)
                    return;

                if (!string.IsNullOrEmpty(type) && !t.types.Contains(type))
                    return;

                string key = $"{StringConvert.SearchName(t.name)}:{StringConvert.SearchName(t.originalname)}";

                HashSet<string> langs;
                if (t.ffprobe != null || !AppInit.conf.tracks)
                    langs = TracksDB.Languages(t, t.ffprobe);
                else
                {
                    var streams = TracksDB.Get(t.magnet, t.types);
                    langs = TracksDB.Languages(t, streams ?? t.ffprobe);
                }

                var model = new Models.TorrentQuality()
                {
                    types = t.types.ToHashSet(),
                    createTime = t.createTime,
                    updateTime = t.updateTime,
                    languages = langs ?? new HashSet<string>(),
                    qualitys = new HashSet<int>() { t.quality }
                };

                if (torrents.TryGetValue(key, out Dictionary<int, Models.TorrentQuality> val))
                {
                    if (val.TryGetValue(t.relased, out Models.TorrentQuality _md))
                    {
                        if (langs != null)
                        {
                            foreach (var item in langs)
                                _md.languages.Add(item);
                        }

                        if (t.types != null)
                        {
                            foreach (var item in t.types)
                                _md.types.Add(item);
                        }

                        _md.qualitys.Add(t.quality);

                        if (_md.createTime > t.createTime)
                            _md.createTime = t.createTime;

                        if (t.updateTime > _md.updateTime)
                            _md.updateTime = t.updateTime;

                        val[t.relased] = _md;
                    }
                    else
                    {
                        val.TryAdd(t.relased, model);
                    }

                    torrents[key] = val;
                }
                else
                {
                    torrents.TryAdd(key, new Dictionary<int, Models.TorrentQuality>() { [t.relased] = model });
                }
            }
            #endregion

            IEnumerable<KeyValuePair<string, MasterDbShard>> mdb = FileDB.masterDb;

            if (!string.IsNullOrEmpty(_s) && !string.IsNullOrEmpty(_so))
            {
                mdb = mdb.Where(i => i.Key.Contains(_s) || i.Key.Contains(_so));
            }
            else if (!string.IsNullOrEmpty(_s))
            {
                mdb = mdb.Where(i => i.Key.Contains(_s));
            }
            else if (!string.IsNullOrEmpty(_so))
            {
                mdb = mdb.Where(i => i.Key.Contains(_so));
            }

            mdb = mdb.OrderByDescending(i => i.Value.updateTime);

            if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
                mdb = mdb.Take(AppInit.conf.maxreadfile);

            var mdbList = mdb.ToList();

            foreach (var val in mdbList)
            {
                foreach (var t in FileDB.OpenRead(val.Key, true).Values)
                    AddTorrents(t);
            }

            if (take == -1)
                return (torrents);
            var orderedTorrents = torrents.OrderByDescending(kvp =>
                kvp.Value.Values.Max(v => v.updateTime)).ToList();

            int skip = (page - 1) * take;
            if (skip < 0) skip = 0;

            var paginated = orderedTorrents.Skip(skip).Take(take);
            var result = paginated.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return (result);
        }
    }
}
