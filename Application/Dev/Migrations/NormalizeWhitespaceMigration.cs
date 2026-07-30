using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacRed.Application.Index;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Utils;
using JacRed.Models.Details;

namespace JacRed.Application.Dev.Migrations
{
    /// <summary>
    /// Приводит пробелы в именах к единому правилу: любой пробельный ряд — один
    /// обычный пробел.
    ///
    /// Зачем понадобилась. Парсеры нормализовали пробелы ТРЕМЯ разными способами,
    /// а строки кода выглядели одинаково — разница пряталась в невидимом U+00A0
    /// внутри набора символов. В базе от этого остались двойные пробелы в именах
    /// (rutor, kinozal) и неразрывные (animelayer). После того как разбор сведён
    /// к одному правилу, старые записи надо подтянуть — иначе при следующем
    /// обходе исправленное имя ляжет в ДРУГОЙ шард, а прежняя запись останется
    /// сиротой: база шардируется по имени.
    ///
    /// Замер 30.07.2026: задето около 0.14% записей.
    ///
    /// Сначала запускать с dryRun=true — посчитает, ничего не трогая.
    /// </summary>
    public sealed class NormalizeWhitespaceMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "normalizeWhitespace";

        static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        public NormalizeWhitespaceMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        public object Run() => Execute(dryRun: false);

        public object DryRun() => Execute(dryRun: true);

        static string Normalize(string value) =>
            string.IsNullOrEmpty(value) ? value : Whitespace.Replace(value, " ").Trim();

        object Execute(bool dryRun)
        {
            long scanned = 0, fixedNames = 0, movedToNewKey = 0, keysEmptied = 0;
            var byTracker = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in FileDB.masterDb.ToArray())
            {
                var shard = dryRun ? null : FileDB.OpenWrite(item.Key);

                try
                {
                    var rows = dryRun
                        ? FileDB.OpenRead(item.Key, cache: false)?.ToList()
                        : shard?.Database.ToList();

                    if (rows == null || rows.Count == 0)
                        continue;

                    var toMigrate = new List<(string url, TorrentDetails torrent, string newKey)>();
                    bool touched = false;

                    foreach (var kv in rows)
                    {
                        var t = kv.Value;
                        if (t == null)
                            continue;

                        scanned++;

                        string name = Normalize(t.name);
                        string originalname = Normalize(t.originalname);

                        if (name == t.name && originalname == t.originalname)
                            continue;

                        fixedNames++;
                        byTracker[t.trackerName ?? "?"] = byTracker.GetValueOrDefault(t.trackerName ?? "?") + 1;

                        if (dryRun)
                        {
                            // В холостом прогоне достаточно понять, сменится ли ключ.
                            string wouldKey = FileDB.KeyForTorrent(name, originalname);
                            if (!string.IsNullOrEmpty(wouldKey) && wouldKey != item.Key)
                                movedToNewKey++;
                            continue;
                        }

                        t.name = name;
                        t.originalname = originalname;
                        t._sn = StringConvert.SearchName(name);
                        t._so = StringConvert.SearchName(originalname);
                        touched = true;

                        string newKey = FileDB.KeyForTorrent(name, originalname);
                        if (!string.IsNullOrEmpty(newKey) && newKey != item.Key && newKey.IndexOf(':') > 0)
                            toMigrate.Add((kv.Key, t, newKey));
                    }

                    if (dryRun)
                        continue;

                    foreach (var (url, torrent, newKey) in toMigrate)
                    {
                        shard.Database.Remove(url);
                        FileDB.MigrateTorrentToNewKey(torrent, newKey);
                        movedToNewKey++;
                    }

                    if (touched)
                        shard.savechanges = true;

                    if (shard.Database.Count == 0)
                    {
                        FileDB.RemoveKeyFromMasterDb(item.Key);
                        keysEmptied++;
                    }
                }
                finally
                {
                    shard?.Dispose();
                }
            }

            if (!dryRun)
            {
                FileDB.SaveChangesToFile();
                TryRebuildFastDb();
            }

            return new
            {
                ok = true,
                dryRun,
                просмотрено = scanned,
                именИсправлено = fixedNames,
                сменилиШард = movedToNewKey,
                ключейОпустело = keysEmptied,
                поТрекерам = byTracker
            };
        }
    }
}
