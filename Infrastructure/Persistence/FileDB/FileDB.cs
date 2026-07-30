using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
            lock (_dbLock)
            {
                AddOrUpdateCore(torrent);
            }
        }

        void AddOrUpdateCore(TorrentBaseDetails torrent)
        {
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

                bool updateFull = false;

                void upt(bool uptfull = false, bool updatetime = true)
                {
                    savechanges = true;

                    if (updatetime)
                    {
                        t.updateTime = DateTime.UtcNow;
                        t.ffprobe_tryingdata = 0;
                    }

                    if (uptfull)
                        updateFull = true;
                }

                #region types
                if (torrent.types != null)
                {
                    if (t.types == null)
                    {
                        t.types = torrent.types;
                        upt(true);
                    }
                    else
                    {
                        foreach (string type in torrent.types)
                        {
                            if (type != null && !t.types.Contains(type))
                                upt(true);
                        }

                        t.types = torrent.types;
                    }
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
                        MigrateTorrentToNewKey(t, newKey);
                        if (Database.Count == 0)
                            RemoveKeyFromMasterDb(fdbkey);
                        return;
                    }
                }
                AddOrUpdateMasterDb(t);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(torrent.magnet) || torrent.types == null || torrent.types.Length == 0)
                    return;

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

            if (openWriteTask.TryGetValue(fdbkey, out WriteTaskModel val))
            {
                val.openconnection -= 1;
                if (0 >= val.openconnection)
                {
                    if (!AppInit.conf.evercache.enable || (AppInit.conf.evercache.enable && AppInit.conf.evercache.validHour > 0))
                        openWriteTask.TryRemove(fdbkey, out _);
                }
            }
        }
        #endregion


    }
}
