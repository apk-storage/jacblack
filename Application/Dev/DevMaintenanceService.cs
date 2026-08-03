using System;
using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Persistence;
using JacBlack.Infrastructure.Utils;
using JacBlack.Models.Details;

namespace JacBlack.Application.Dev
{
    public class DevMaintenanceService : IDevMaintenanceService
    {

        /// <summary>
        /// Пересчитывает размер в байтах из строки размера у всех записей.
        ///
        /// Раньше это действие ставило `updateTime = сейчас` КАЖДОЙ записи и
        /// переписывало все шарды подряд. Последствия были бы неприятные:
        /// стёрлась бы вся картина свежести базы (по ней работает фоновая
        /// проверка живости), а все, кто с нами синхронизируется, увидели бы
        /// миллион «обновлённых» записей и выкачали бы их заново.
        ///
        /// Размер — величина ПРОИЗВОДНАЯ от строки размера, которая не менялась.
        /// Это починка, а не обновление, поэтому updateTime не трогаем и пишем
        /// только те шарды, где что-то действительно поменялось.
        /// </summary>
        public object UpdateSize()
        {
            long changed = 0, unchanged = 0, shardsWritten = 0;

            foreach (var item in FileDB.masterDb.OrderBy(i => i.Value.fileTime).ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    bool touched = false;

                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            touched = true;
                            continue;
                        }

                        long size = Infrastructure.Parsing.SizeParser.ToBytes(torrent.Value.sizeName);
                        if (size == torrent.Value.size)
                        {
                            unchanged++;
                            continue;
                        }

                        torrent.Value.size = size;
                        changed++;
                        touched = true;
                    }

                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);

                    if (touched)
                    {
                        fdb.savechanges = true;
                        shardsWritten++;
                    }
                }
            }

            FileDB.SaveChangesToFile();
            return new { ok = true, исправлено = changed, безИзменений = unchanged, шардовПереписано = shardsWritten };
        }

        public object ResetCheckTime()
        {

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }
                        torrent.Value.checkTime = DateTime.Today.AddDays(-1);
                    }
                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);

                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            return new { ok = true };
        }

        /// <summary>
        /// Пересчитывает производные поля (качество, озвучки, сезоны, размер)
        /// из уже сохранённых заголовков.
        ///
        /// Как и у UpdateSize, здесь раньше проставлялся `updateTime = сейчас`
        /// всем записям подряд. Это не обновление данных с трекера, а пересчёт
        /// из того, что уже лежит, — отметку времени не трогаем, иначе рушится
        /// картина свежести базы и синхронизирующиеся выкачивают всё заново.
        /// </summary>
        public object UpdateDetails()
        {
            long processed = 0;

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }

                        FileDB.updateFullDetails(torrent.Value);
                        torrent.Value.languages = null;
                        processed++;
                    }

                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);

                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            return new { ok = true, пересчитано = processed };
        }

        public object UpdateSearchName()
        {

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var keysToRemove = new List<string>();
                    var toMigrate = new List<(string url, TorrentDetails t, string newKey)>();
                    foreach (var torrent in fdb.Database)
                    {
                        if (torrent.Value == null)
                        {
                            keysToRemove.Add(torrent.Key);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(torrent.Value.name))
                            torrent.Value.name = torrent.Value.title ?? "";

                        if (string.IsNullOrWhiteSpace(torrent.Value.originalname))
                            torrent.Value.originalname = torrent.Value.title ?? torrent.Value.name ?? "";
                        torrent.Value._sn = StringConvert.SearchName(torrent.Value.name);
                        torrent.Value._so = StringConvert.SearchName(torrent.Value.originalname);
                        // Если ключ бакета изменился (например починили name) — переносим торрент в правильный бакет, чтобы поиск находил по новому ключу
                        string newKey = FileDB.KeyForTorrent(torrent.Value.name, torrent.Value.originalname);
                        if (!string.IsNullOrEmpty(newKey) && newKey != item.Key && newKey.IndexOf(':') > 0)
                            toMigrate.Add((torrent.Key, torrent.Value, newKey));
                    }
                    foreach (var k in keysToRemove)
                        fdb.Database.Remove(k);
                    foreach (var (url, t, newKey) in toMigrate)
                    {
                        fdb.Database.Remove(url);
                        FileDB.MigrateTorrentToNewKey(t, newKey);
                    }
                    if (fdb.Database.Count == 0)
                        FileDB.RemoveKeyFromMasterDb(item.Key);
                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            return new { ok = true };
        }

    }
}
