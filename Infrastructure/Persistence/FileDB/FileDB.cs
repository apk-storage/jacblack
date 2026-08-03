using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using JacRed.Infrastructure.Indexers;
using JacRed.Infrastructure.Parsing;
using JacRed.Infrastructure.Utils;
using JacRed.Models;
using JacRed.Models.Details;
using Newtonsoft.Json;
using JacRed.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Persistence
{
    public partial class FileDB : IDisposable
    {
        #region FileDB
        string fdbkey;

        public bool savechanges = false;

        readonly object _dbLock = new object();

        FileDB(string key)
        {
            fdbkey = key;
            string fdbpath = pathDb(key);

            if (File.Exists(fdbpath))
                Database = JsonStream.Read<Dictionary<string, TorrentDetails>>(fdbpath) ?? new Dictionary<string, TorrentDetails>();
        }

        public Dictionary<string, TorrentDetails> Database = new Dictionary<string, TorrentDetails>();

        internal Dictionary<string, TorrentDetails> GetSnapshot()
        {
            lock (_dbLock)
                return new Dictionary<string, TorrentDetails>(Database);
        }

        /// <summary>
        /// Записывает результат разбора дорожек. Вынесено сюда из статического
        /// метода по двум причинам.
        ///
        /// Первая: перебор шёл по ЖИВОМУ словарю мимо замка, тогда как всё
        /// остальное работает под ним. Достаточно было одновременной записи в
        /// тот же шард, чтобы перебор упал с «коллекция изменена».
        ///
        /// Вторая: разбор дорожек поднимал updateTime, то есть запись
        /// выглядела только что обойдённой, хотя на трекер никто не ходил. Тот
        /// же изъян уже убран у обновления размера и подробностей — свежесть
        /// должен менять обход, а не служебная работа.
        /// </summary>
        internal void ApplyFfprobe(string magnet, int ffprobeTryingData, JacRed.Models.Tracks.FfprobeModel ffprobeResult)
        {
            lock (_dbLock)
            {
                var torrent = Database.Values.FirstOrDefault(t =>
                    !string.IsNullOrEmpty(t.magnet) &&
                    t.magnet.Equals(magnet, StringComparison.OrdinalIgnoreCase));

                if (torrent == null)
                    return;

                bool updated = false;

                if (torrent.ffprobe_tryingdata != ffprobeTryingData)
                {
                    torrent.ffprobe_tryingdata = ffprobeTryingData;
                    updated = true;
                }

                if (ffprobeResult?.streams != null && ffprobeResult.streams.Count > 0)
                {
                    torrent.ffprobe = ffprobeResult.streams;
                    updated = true;
                }

                if (updated)
                    savechanges = true;
            }
        }

        internal void SaveChangesIfNeeded()
        {
            lock (_dbLock)
            {
                if (Database.Count > 0 && savechanges)
                {
                    string path = pathDb(fdbkey);
                    EnsureShardDir(path);
                    JsonStream.Write(path, Database);
                }
            }
        }
        #endregion

        #region AddOrUpdate

        public void AddOrUpdate(TorrentBaseDetails torrent)
        {
            TorrentDetails migrate;
            string newKey;

            lock (_dbLock)
                migrate = AddOrUpdateCore(torrent, out newKey);

            // Переезд в чужой шард делаем ЗА пределами замка. Раньше он шёл
            // изнутри: поток держал замок шарда A и брал замок шарда B, а
            // встречный переезд B→A в это же время держал B и ждал A —
            // классическая взаимная блокировка, которая повесила бы обход
            // намертво и без единой записи в журнале.
            if (migrate != null)
                MigrateTorrentToNewKey(migrate, newKey);
        }

        /// <summary>
        /// Возвращает запись, которую нужно перенести в другой шард, и ключ
        /// этого шарда. Сам перенос — забота вызывающего: он делается уже без
        /// замка (см. <see cref="AddOrUpdate(TorrentBaseDetails)"/>).
        /// </summary>
        TorrentDetails AddOrUpdateCore(TorrentBaseDetails torrent, out string migrateToKey)
        {
            migrateToKey = null;
            bool foundById = false;
            if (!Database.TryGetValue(torrent.url, out TorrentDetails t))
            {
                int torrentId = GetTorrentIdFromUrl(torrent.trackerName, torrent.url);
                if (torrentId > 0)
                {
                    var sameTrackerEntries = Database
                        .Where(kv => string.Equals(kv.Value.trackerName, torrent.trackerName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var kv in sameTrackerEntries)
                    {
                        // Check if existing torrent has same tracker and same ID
                        int existingId = GetTorrentIdFromUrl(torrent.trackerName, kv.Key);
                        if (existingId == torrentId)
                        {
                            Database.Remove(kv.Key);
                            t = kv.Value;
                            t.url = torrent.url;
                            foundById = true;
                            break;
                        }
                    }
                }
            }

            if (t != null)
            {
                // Считаем здесь, а не в парсерах: через это место проходят записи
                // всех семнадцати источников, а поштучный лог ведут только шесть.
                ParseCounters.Updated(torrent.trackerName);

                // Отдельно от счётчиков: они живут один прогон, а это переживает
                // перезапуск и отвечает на вопрос «источник ещё что-то выкладывает».
                TrackerFreshness.NoteSeen(torrent.trackerName, torrent.createTime);

                bool updateFull = false;
                bool changed = false;

                void upt(bool uptfull = false, bool updatetime = true)
                {
                    savechanges = true;
                    changed = true;

                    if (updatetime)
                    {
                        t.updateTime = DateTime.UtcNow;
                        t.ffprobe_tryingdata = 0;
                    }

                    if (uptfull)
                        updateFull = true;
                }

                #region types
                // Раньше набор типов присваивался БЕЗ отметки об изменении, если
                // он только сузился: upt() звали лишь при появлении нового типа.
                // Запись при этом молча менялась в памяти, а на диск не просилась
                // — правка терялась, пока её случайно не выносило соседнее поле.
                if (torrent.types != null && (t.types == null || !t.types.SequenceEqual(torrent.types)))
                {
                    t.types = torrent.types;
                    upt(true);
                }
                #endregion

                if (torrent.trackerName != t.trackerName)
                {
                    t.trackerName = torrent.trackerName;
                    upt(true);
                }

                if (torrent.title != t.title)
                {
                    t.title = torrent.title;
                    upt(true);
                }

                if (torrent.createTime != default && torrent.createTime > t.createTime)
                {
                    t.createTime = torrent.createTime;
                    upt(updatetime: false);
                }

                if (!string.IsNullOrWhiteSpace(torrent.magnet) && torrent.magnet != t.magnet)
                {
                    t.ffprobe_tryingdata = 0;
                    t.ffprobe = null;
                    t.magnet = torrent.magnet;
                    upt();
                }

                if (torrent.sid != t.sid)
                {
                    if (t.sid == 0 && torrent.sid >= 2 && t.ffprobe_tryingdata >= AppInit.conf.tracksatempt)
                    {
                        t.ffprobe_tryingdata = 0;
                    }
                    t.sid = torrent.sid;
                    upt(updatetime: false);
                }

                if (torrent.pir != t.pir)
                {
                    t.pir = torrent.pir;
                    upt(updatetime: false);
                }

                if (!string.IsNullOrWhiteSpace(torrent.sizeName) && torrent.sizeName != t.sizeName)
                {
                    t.sizeName = torrent.sizeName;
                    upt(true);
                }

                if (!string.IsNullOrWhiteSpace(torrent.name) && torrent.name != t.name)
                {
                    t.name = torrent.name;
                    t._sn = StringConvert.SearchName(t.name);
                    upt();
                }
                else if (string.IsNullOrWhiteSpace(t.name) && !string.IsNullOrWhiteSpace(torrent.title))
                {
                    t.name = torrent.title;
                    t._sn = StringConvert.SearchName(t.name);
                    upt();
                }
                // Убеждаемся, что _sn всегда заполнен, даже если name не изменился
                if (string.IsNullOrWhiteSpace(t._sn))
                {
                    if (!string.IsNullOrWhiteSpace(t.name))
                        t._sn = StringConvert.SearchName(t.name);
                    else if (!string.IsNullOrWhiteSpace(torrent.title))
                        t._sn = StringConvert.SearchName(torrent.title);

                    if (!string.IsNullOrWhiteSpace(t._sn))
                        upt();
                }

                if (!string.IsNullOrWhiteSpace(torrent.originalname) && torrent.originalname != t.originalname)
                {
                    t.originalname = torrent.originalname;
                    t._so = StringConvert.SearchName(t.originalname);
                    upt();
                }
                else if (string.IsNullOrWhiteSpace(t.originalname))
                {
                    // For Russian content where originalname is null, use name instead of title
                    // to avoid creating keys with full title (including season/episode info)
                    t.originalname = !string.IsNullOrWhiteSpace(t.name) ? t.name : (torrent.title ?? "");
                    t._so = StringConvert.SearchName(t.originalname);
                    upt();
                }
                // Убеждаемся, что _so всегда заполнен, даже если originalname не изменился
                if (string.IsNullOrWhiteSpace(t._so))
                {
                    if (!string.IsNullOrWhiteSpace(t.originalname))
                        t._so = StringConvert.SearchName(t.originalname);
                    else if (!string.IsNullOrWhiteSpace(t.name))
                        t._so = StringConvert.SearchName(t.name);
                    else if (!string.IsNullOrWhiteSpace(torrent.title))
                        t._so = StringConvert.SearchName(torrent.title);

                    if (!string.IsNullOrWhiteSpace(t._so))
                        upt();
                }

                if (torrent.relased > 0 && torrent.relased != t.relased)
                {
                    t.relased = torrent.relased;
                    upt();
                }

                // Код IMDB приходит не от всех источников. Если хоть один его
                // сообщил — сохраняем и больше не затираем пустым: у русских
                // трекеров кода нет, и обновление оттуда не должно его стирать.
                if (!string.IsNullOrWhiteSpace(torrent.imdb) && torrent.imdb != t.imdb)
                {
                    t.imdb = torrent.imdb;
                    upt();
                }

                if (!string.IsNullOrWhiteSpace(t.imdb))
                {
                    ImdbIndex.Remember(t.imdb, t.name, t.originalname, t.relased);
                }
                else if (t.relased > 1900)
                {
                    // Код сообщают три источника из двадцати, но фильм один и тот
                    // же: если его принесла хоть одна раздача, подтягиваем к
                    // остальным по оригинальному названию и году.
                    if (ImdbIndex.TryGetByTitle(t.originalname, t.relased, out string known)
                        || ImdbIndex.TryGetByTitle(t.name, t.relased, out known))
                    {
                        t.imdb = known;
                        upt(updatetime: false);
                    }
                }

                if (torrent.ffprobe != null && t.ffprobe == null)
                {
                    t.ffprobe = torrent.ffprobe;
                    upt();
                }

                if (updateFull)
                {
                    updateFullDetails(t);
                    if (AppInit.conf.logFdb)
                        AppendFdbLog(torrent, t);
                }
                else if (AppInit.conf.logFdb)
                    AppendFdbLog(torrent, t);

                if (changed)
                    TrackerFreshness.NoteChanged(t.trackerName);

                // Было DateTime.Now при том, что createTime и updateTime в этой же
                // записи пишутся в UTC. Смесь двух часов в одной записи уже
                // приводила к неверным выводам при разборе.
                t.checkTime = DateTime.UtcNow;

                if (foundById)
                    Database.TryAdd(t.url, t);

                // Drop legacy bare episode/movie URL once a #quality row is stored.
                if (string.Equals(t.trackerName, "lostfilm", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(t.url) && t.url.Contains('#', StringComparison.Ordinal))
                {
                    string bare = t.url.Substring(0, t.url.IndexOf('#'));
                    if (!string.IsNullOrEmpty(bare) && Database.ContainsKey(bare))
                    {
                        Database.Remove(bare);
                        savechanges = true;
                    }
                }

                // База шардируется по имени, поэтому запись, чьё имя изменилось,
                // обязана переехать в шард своего ключа. Раньше это делалось
                // ТОЛЬКО для lostfilm, а у остальных шестнадцати источников
                // записи оставались сиротами в чужих шардах — отсюда и разовые
                // миграции FixKnabenNames, FixBitruNames, FixAnimeToshoNames,
                // которые лечили следствие. Теперь правило общее для всех.
                {
                    string newKey = keyDb(t.name, t.originalname);
                    if (!string.IsNullOrEmpty(newKey) && newKey != fdbkey && newKey.IndexOf(':') > 0)
                    {
                        Database.Remove(t.url);
                        savechanges = true;
                        if (Database.Count == 0)
                            RemoveKeyFromMasterDb(fdbkey);

                        migrateToKey = newKey;
                        return t;
                    }
                }
                AddOrUpdateMasterDb(t);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(torrent.magnet) || torrent.types == null || torrent.types.Length == 0)
                    return null;

                var name = torrent.name ?? torrent.title ?? "";
                // For Russian content where originalname is null, use name instead of title
                // to avoid creating keys with full title (including season/episode info)
                var originalname = torrent.originalname ?? name ?? "";

                // Убеждаемся, что name и originalname не пустые
                if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(torrent.title))
                    name = torrent.title;
                if (string.IsNullOrWhiteSpace(originalname))
                    originalname = name ?? torrent.title ?? "";

                t = new TorrentDetails()
                {
                    url = torrent.url,
                    types = torrent.types,
                    trackerName = torrent.trackerName,
                    createTime = torrent.createTime,
                    updateTime = torrent.updateTime,
                    title = torrent.title,
                    name = name,
                    originalname = originalname,
                    pir = torrent.pir,
                    sid = torrent.sid,
                    relased = torrent.relased,
                    sizeName = torrent.sizeName,
                    magnet = torrent.magnet,
                    ffprobe = torrent.ffprobe,
                    imdb = torrent.imdb
                };

                    if (!string.IsNullOrWhiteSpace(t.imdb))
                        ImdbIndex.Remember(t.imdb, t.name, t.originalname, t.relased);

                // Всегда заполняем _sn и _so, даже если name или originalname пустые
                // Используем fallback на title если нужно
                t._sn = StringConvert.SearchName(t.name);
                if (string.IsNullOrWhiteSpace(t._sn) && !string.IsNullOrWhiteSpace(t.title))
                    t._sn = StringConvert.SearchName(t.title);

                t._so = StringConvert.SearchName(t.originalname);
                if (string.IsNullOrWhiteSpace(t._so))
                {
                    // Если originalname пустое, используем name или title
                    if (!string.IsNullOrWhiteSpace(t.name))
                        t._so = StringConvert.SearchName(t.name);
                    else if (!string.IsNullOrWhiteSpace(t.title))
                        t._so = StringConvert.SearchName(t.title);
                }

                savechanges = true;
                updateFullDetails(t);

                if (AppInit.conf.logFdb)
                    AppendFdbLog(torrent, t);

                Database.TryAdd(t.url, t);
                AddOrUpdateMasterDb(t);
                ParseCounters.Added(t.trackerName);
                TrackerFreshness.NoteSeen(t.trackerName, t.createTime);
                TrackerFreshness.NoteAdded(t.trackerName);

                // Drop legacy bare episode/movie URL once a #quality row is stored.
                if (string.Equals(t.trackerName, "lostfilm", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(t.url) && t.url.Contains('#', StringComparison.Ordinal))
                {
                    string bare = t.url.Substring(0, t.url.IndexOf('#'));
                    if (!string.IsNullOrEmpty(bare) && Database.ContainsKey(bare))
                    {
                        Database.Remove(bare);
                        savechanges = true;
                    }
                }
            }

            return null;
        }
        #endregion

        #region FdbLog
        static readonly string FdbLogDir = "Data/log";
        const string FdbLogPrefix = "fdb.";

        static void AppendFdbLog(TorrentBaseDetails torrent, TorrentDetails t)
        {
            try
            {
                if (!Directory.Exists(FdbLogDir))
                    Directory.CreateDirectory(FdbLogDir);

                int retentionDays = AppInit.conf?.logFdbRetentionDays ?? 0;
                if (retentionDays > 0)
                {
                    var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);
                    foreach (var path in Directory.EnumerateFiles(FdbLogDir, FdbLogPrefix + "*.log"))
                    {
                        string name = Path.GetFileNameWithoutExtension(path);
                        if (name.Length > FdbLogPrefix.Length && DateTime.TryParseExact(name.Substring(FdbLogPrefix.Length), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fileDate) && fileDate < cutoff)
                        {
                            try
                            {
                                File.Delete(path);
                            }
                            catch (Exception ex)
                            {
                                // Просроченный файл останется лежать. Разово не страшно,
                                // но если повторяется — уборка не работает и диск растёт.
                                JacRedLog.Swallowed(JacRedLogCategories.Fdb,
                                    $"не удалился просроченный лог {Path.GetFileName(path)}", ex, LogLevel.Debug);
                            }
                        }
                    }
                }

                string logPath = Path.Combine(FdbLogDir, FdbLogPrefix + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log");
                string jsonLine = JsonConvert.SerializeObject(new List<TorrentBaseDetails>() { torrent, t }, Formatting.None) + "\n";
                File.AppendAllText(logPath, jsonLine);

                PurgeFdbLogBySizeAndCount();
            }
            catch (Exception ex)
            {
                // Диагностический лог базы вести не удалось. Сама запись в базу уже
                // прошла, поэтому работу не рвём — но раньше это выглядело так,
                // будто лог просто пуст.
                JacRedLog.Swallowed(JacRedLogCategories.Fdb, "не записался лог изменений базы", ex);
            }
        }

        static void PurgeFdbLogBySizeAndCount()
        {
            int maxSizeMb = AppInit.conf?.logFdbMaxSizeMb ?? 0;
            int maxFiles = AppInit.conf?.logFdbMaxFiles ?? 0;
            if (maxSizeMb <= 0 && maxFiles <= 0)
                return;
            try
            {
                long maxBytes = maxSizeMb > 0 ? (long)maxSizeMb * 1024 * 1024 : long.MaxValue;
                var list = new List<(string path, long length, DateTime date)>();
                foreach (var path in Directory.EnumerateFiles(FdbLogDir, FdbLogPrefix + "*.log"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (name.Length <= FdbLogPrefix.Length || !DateTime.TryParseExact(name.Substring(FdbLogPrefix.Length), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fileDate))
                        continue;
                    long len = 0;
                    try
                    {
                        len = new FileInfo(path).Length;
                    }
                    catch (Exception ex)
                    {
                        // Файл посчитается нулевым, и общий размер выйдет заниженным —
                        // то есть уборка недоберёт. Молча такое не отследить.
                        JacRedLog.Swallowed(JacRedLogCategories.Fdb,
                            $"не прочитался размер {Path.GetFileName(path)}", ex, LogLevel.Debug);
                    }
                    list.Add((path, len, fileDate));
                }
                list.Sort((a, b) => a.date.CompareTo(b.date));
                long total = list.Sum(x => x.length);
                int count = list.Count;
                foreach (var item in list)
                {
                    if (total <= maxBytes && count <= maxFiles)
                        break;
                    try
                    {
                        File.Delete(item.path);
                        total -= item.length;
                        count--;
                    }
                    catch (Exception ex)
                    {
                        JacRedLog.Swallowed(JacRedLogCategories.Fdb,
                            $"не удалился лог {Path.GetFileName(item.path)} при уборке по размеру", ex, LogLevel.Debug);
                    }
                }
            }
            catch (Exception ex)
            {
                // Уборка логов целиком не отработала — значит ограничения по размеру
                // и числу файлов не действуют. Именно так лог однажды вырос до 3.4 ГБ.
                JacRedLog.Swallowed(JacRedLogCategories.Fdb, "уборка логов базы не отработала", ex);
            }
        }
        #endregion

        #region Dispose
        public void Dispose()
        {
            SaveChangesIfNeeded();

            // Отпускаем ИМЕННО свою запись в кеше. Раньше проверки на тождество
            // не было, и разовый экземпляр (его создаёт чтение мимо кеша) при
            // закрытии уменьшал счётчик ЧУЖОГО, живого шарда — вплоть до
            // вытеснения того из-под работающих с ним потоков.
            if (!openWriteTask.TryGetValue(fdbkey, out WriteTaskModel val) || !ReferenceEquals(val.db, this))
                return;

            lock (val)
            {
                val.openconnection -= 1;
                if (val.openconnection > 0)
                    return;

                val.openconnection = 0;

                if (!AppInit.conf.evercache.enable || AppInit.conf.evercache.validHour > 0)
                    openWriteTask.TryRemove(fdbkey, out _);
            }
        }
        #endregion


    }
}
