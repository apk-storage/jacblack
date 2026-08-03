using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using JacRed.Infrastructure.Utils;
using JacRed.Infrastructure.Networking;
using JacRed.Infrastructure.Logging;
using JacRed.Models;
using JacRed.Models.Details;
using Microsoft.Extensions.Logging;

namespace JacRed.Infrastructure.Persistence
{
    public partial class FileDB
    {
        #region FileDB
        /// <summary>
        /// $"{search_name}:{search_originalname}"
        /// Верхнее время изменения
        /// </summary>
        public static ConcurrentDictionary<string, MasterDbShard> masterDb = new ConcurrentDictionary<string, MasterDbShard>();

        static ConcurrentDictionary<string, WriteTaskModel> openWriteTask = new ConcurrentDictionary<string, WriteTaskModel>();

        /// <summary>
        /// Поднимает индекс базы: сам файл, а если его нет или он не читается —
        /// суточную копию за сегодня, затем за вчера.
        ///
        /// Отсюда убран переход со схемы от 29.08.2023, где значением была
        /// голая дата вместо <see cref="MasterDbShard"/>. Живых баз в той схеме
        /// не осталось — проверено 30.07.2026 на рабочей, все 349 921 ключа
        /// новые, — а код был не бесплатным: на каждом запуске он пробовал
        /// разобрать индекс заведомо негодной схемой и глушил исключение.
        /// </summary>
        static FileDB()
        {
            if (File.Exists("Data/masterDb.bz"))
                masterDb = JsonStream.Read<ConcurrentDictionary<string, MasterDbShard>>("Data/masterDb.bz");

            if (masterDb != null)
                return;

            if (File.Exists($"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz"))
                masterDb = JsonStream.Read<ConcurrentDictionary<string, MasterDbShard>>($"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz");

            if (masterDb == null && File.Exists($"Data/masterDb_{DateTime.Today.AddDays(-1):dd-MM-yyyy}.bz"))
                masterDb = JsonStream.Read<ConcurrentDictionary<string, MasterDbShard>>($"Data/masterDb_{DateTime.Today.AddDays(-1):dd-MM-yyyy}.bz");

            if (masterDb == null)
                masterDb = new ConcurrentDictionary<string, MasterDbShard>();

            // Индекса не было или он не прочитался — отметка о последней
            // синхронизации больше ничему не соответствует.
            if (File.Exists(Path.Combine("Data", "temp", "lastsync.txt")))
                File.Delete(Path.Combine("Data", "temp", "lastsync.txt"));
        }
        #endregion

        #region pathDb / keyDb
        /// <summary>
        /// Путь к шарду. Раньше здесь на КАЖДОЕ обращение к базе — и на чтение
        /// тоже — вызывался Directory.CreateDirectory, то есть системный вызов
        /// там, где нужна только склейка строки. Каталоги создаются отдельно,
        /// перед записью, и запоминаются.
        /// </summary>
        static string pathDb(string key)
        {
            string md5key = HashTo.md5(key);

            if (AppInit.conf.fdbPathLevels == 2)
                return $"Data/fdb/{md5key.Substring(0, 2)}/{md5key.Substring(2)}";

            return $"Data/fdb/{md5key[0]}/{md5key}";
        }

        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _createdDirs = new();

        /// <summary>Создаёт каталог шарда, если он ещё не создавался в этом запуске.</summary>
        static void EnsureShardDir(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir) || !_createdDirs.TryAdd(dir, 0))
                return;

            try { Directory.CreateDirectory(dir); }
            catch (IOException) { _createdDirs.TryRemove(dir, out _); }
            catch (UnauthorizedAccessException) { _createdDirs.TryRemove(dir, out _); }
        }

        static string keyDb(string name, string originalname)
        {
            string search_name = StringConvert.SearchName(name);
            string search_originalname = StringConvert.SearchName(originalname);

            // Если search_name или search_originalname null, используем fallback
            // Это важно для случаев, когда name или originalname пустые после нормализации
            if (string.IsNullOrWhiteSpace(search_name))
            {
                // Пробуем использовать originalname если name пустое
                if (!string.IsNullOrWhiteSpace(search_originalname))
                    search_name = search_originalname;
                else
                    // Если оба пустые, используем пустую строку вместо null
                    search_name = "";
            }

            if (string.IsNullOrWhiteSpace(search_originalname))
            {
                // Пробуем использовать name если originalname пустое
                if (!string.IsNullOrWhiteSpace(search_name))
                    search_originalname = search_name;
                else
                    // Если оба пустые, используем пустую строку вместо null
                    search_originalname = "";
            }

            return $"{search_name}:{search_originalname}";
        }

        /// <summary>Ключ бакета по name/originalname (для поиска и миграции).</summary>
        public static string KeyForTorrent(string name, string originalname) => keyDb(name, originalname);

        #endregion

        /// <summary>Перенос торрента в бакет с ключом newKey (после смены name/originalname). Вызывается из FileDB и из DevMaintenanceService.UpdateSearchName.</summary>
        public static void MigrateTorrentToNewKey(TorrentDetails t, string newKey)
        {
            using (var fdb = OpenWrite(newKey))
            {
                fdb.AddOrUpdate(t);
            }
        }

        /// <summary>Удаляет ключ из masterDb (например после миграции, когда бакет опустел). Вызывать только если бакет действительно пуст.</summary>
        public static void RemoveKeyFromMasterDb(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            masterDb.TryRemove(key, out _);
        }

        #region AddOrUpdateMasterDb
        static void AddOrUpdateMasterDb(TorrentDetails torrent)
        {
            string key = keyDb(torrent.name, torrent.originalname);
            var md = new MasterDbShard() { updateTime = torrent.updateTime, fileTime = torrent.updateTime.ToFileTimeUtc() };

            if (masterDb.TryGetValue(key, out MasterDbShard info))
            {
                if (torrent.updateTime > info.updateTime)
                    masterDb[key] = md;
            }
            else
            {
                masterDb.TryAdd(key, md);
            }
        }
        #endregion

        #region OpenRead / OpenWrite
        /// <summary>
        /// Берёт из кеша запись о шарде — или заводит её, если шард ещё не
        /// открыт, — и отдаёт под собственным замком этой записи.
        ///
        /// Зачем такая осторожность. Раньше открытие шло через TryGetValue +
        /// TryAdd, и два потока, разошедшиеся на этой паре, получали ДВА разных
        /// экземпляра одного шарда: победитель попадал в кеш, а проигравшему
        /// возвращали его собственный. Дальше оба читали один файл, писали в
        /// свои копии и по очереди сохраняли — чьи записи легли вторыми, те и
        /// оставались, остальные пропадали без следа. Случай не выдуманный:
        /// один и тот же фильм приходит с разных трекеров, а ключ шарда
        /// строится по названию, поэтому параллельные обходы регулярно метят
        /// в один шард.
        ///
        /// Второе, что закрывает замок, — вытеснение из кеша между «нашли» и
        /// «взяли»: вытесненный экземпляр уже сохранён и никем не разделяется,
        /// писать в него нельзя. Проверка тождества под замком это ловит.
        /// </summary>
        static WriteTaskModel AcquireShard(string key, bool forWrite, bool update_lastread)
        {
            while (true)
            {
                var wtm = openWriteTask.GetOrAdd(key, k => new WriteTaskModel { db = new FileDB(k), openconnection = 0 });

                lock (wtm)
                {
                    if (!openWriteTask.TryGetValue(key, out var current) || !ReferenceEquals(current, wtm))
                        continue;

                    if (forWrite)
                        wtm.openconnection += 1;

                    if (update_lastread)
                    {
                        wtm.countread++;
                        wtm.lastread = DateTime.UtcNow;
                    }

                    return wtm;
                }
            }
        }

        /// <summary>Always returns a snapshot — never expose live Database to concurrent readers.</summary>
        public static IReadOnlyDictionary<string, TorrentDetails> OpenRead(string key, bool update_lastread = false, bool cache = true)
        {
            if (AppInit.conf.evercache.enable && (cache || AppInit.conf.evercache.validHour == 0))
                return AcquireShard(key, forWrite: false, update_lastread).db.GetSnapshot();

            if (openWriteTask.TryGetValue(key, out WriteTaskModel val))
            {
                lock (val)
                {
                    if (update_lastread)
                    {
                        val.countread++;
                        val.lastread = DateTime.UtcNow;
                    }
                }

                return val.db.GetSnapshot();
            }

            // Кеш выключен: читаем разово, в общий список не заводим.
            return new FileDB(key).GetSnapshot();
        }

        public static FileDB OpenWrite(string key) => AcquireShard(key, forWrite: true, update_lastread: false).db;
        #endregion

        /// <summary>
        /// Годится ли раздача по типу содержимого.
        ///
        /// Лампа работает с TMDB, поэтому всё, чего в TMDB нет, до человека
        /// не дойдёт: спортивные трансляции, книги, музыка, программы.
        /// Замер 29.07.2026 показал в базе 2.5% спорта (около 27 тысяч
        /// записей), почти весь с rutor — они лежали мёртвым грузом.
        ///
        /// Пустой список типов пропускаем: часть трекеров тип не проставляет,
        /// и молча терять такие раздачи хуже, чем оставить лишнее.
        /// </summary>
        public static bool IsWantedContent(string[] types)
        {
            if (types == null || types.Length == 0)
                return true;

            var wanted = AppInit.conf?.contentTypes;
            if (wanted == null || wanted.Length == 0)
                return true;

            foreach (string type in types)
            {
                foreach (string ok in wanted)
                {
                    if (string.Equals(type, ok, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        #region AddOrUpdate
        public static void AddOrUpdate(IReadOnlyCollection<TorrentBaseDetails> torrents)
        {
            _ = AddOrUpdate(torrents, null);
        }

        async public static ValueTask AddOrUpdate<T>(IReadOnlyCollection<T> torrents, Func<T, IReadOnlyDictionary<string, TorrentDetails>, Task<bool>> predicate) where T : TorrentBaseDetails
        {
            var temp = new Dictionary<string, List<T>>();

            foreach (var torrent in torrents)
            {
                // Берём только то, что есть в TMDB: Лампа работает с ней, и
                // раздача, которой там нет, до человека всё равно не дойдёт.
                // Отсекаем здесь, в единственной точке входа в базу, — тогда
                // правило работает и для трекеров, которые появятся потом.
                if (!IsWantedContent(torrent.types))
                    continue;

                string key = keyDb(torrent.name, torrent.originalname);
                if (!temp.ContainsKey(key))
                    temp.Add(key, new List<T>());

                temp[key].Add(torrent);
            }

            foreach (var t in temp)
            {
                using (var fdb = OpenWrite(t.Key))
                {
                    foreach (var torrent in t.Value)
                    {
                        if (predicate != null)
                        {
                            if (await predicate.Invoke(torrent, fdb.Database) == false)
                                continue;
                        }

                        fdb.AddOrUpdate(torrent);
                    }
                }
            }
        }
        #endregion

        #region SaveChangesToFile
        public static void SaveChangesToFile()
        {
            try
            {
                JsonStream.Write("Data/masterDb.bz", masterDb);

                // Словарь кодов IMDB сохраняем здесь же: он пополняется теми же
                // записями и должен переживать перезапуск вместе с базой.
                ImdbIndex.SaveIfDirty();

                // Отметки свежести источников — оттуда же и по той же причине.
                Indexers.TrackerFreshness.SaveIfDirty();

                if (!File.Exists($"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz"))
                    File.Copy("Data/masterDb.bz", $"Data/masterDb_{DateTime.Today:dd-MM-yyyy}.bz");

                if (File.Exists($"Data/masterDb_{DateTime.Today.AddDays(-3):dd-MM-yyyy}.bz"))
                    File.Delete($"Data/masterDb_{DateTime.Today.AddDays(-3):dd-MM-yyyy}.bz");
            }
            catch (Exception ex)
            {
                // Здесь пишется индекс всей базы и его суточная копия. Молчание
                // означало бы, что копии просто нет, а узнать об этом было бы
                // не от кого — до дня, когда она понадобится.
                JacRedLog.Swallowed(JacRedLogCategories.Fdb, "запись masterDb и суточной копии", ex, LogLevel.Error);
            }
        }
        #endregion

        ///by Lexandros
        /// <summary>
        /// Обновляет информацию о попытках анализа ffprobe для торрента
        /// </summary>
        /// <param name="torrentKey">Ключ торрента в базе (search_name:search_originalname)</param>
        /// <param name="magnet">Magnet-ссылка торрента для поиска</param>
        /// <param name="ffprobeTryingData">Новое значение счетчика попыток</param>
        /// <param name="ffprobeResult">Результаты анализа ffprobe (опционально)</param>
        public static void UpdateTorrentFfprobeInfo(string torrentKey, string magnet, int ffprobeTryingData, JacRed.Models.Tracks.FfprobeModel ffprobeResult = null)
        {
            if (string.IsNullOrEmpty(torrentKey) || string.IsNullOrEmpty(magnet))
                return;

            try
            {
                using (var fdb = OpenWrite(torrentKey))
                    fdb.ApplyFfprobe(magnet, ffprobeTryingData, ffprobeResult);
            }
            catch (Exception ex)
            {
                JacRedLog.Error(JacRedLogCategories.Fdb, $"Ошибка при обновлении ffprobe информации: {ex.Message}");
            }
        }


    }
}
