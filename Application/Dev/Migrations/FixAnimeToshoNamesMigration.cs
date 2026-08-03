using System;
using System.Collections.Generic;
using System.Linq;
using JacBlack.Application.Index;
using JacBlack.Infrastructure.Persistence;
using JacBlack.Infrastructure.Trackers.AnimeTosho;
using JacBlack.Infrastructure.Utils;
using JacBlack.Models.Details;

namespace JacBlack.Application.Dev.Migrations
{
    /// <summary>
    /// Пересчитывает имена раздач AnimeTosho по актуальному разбору заголовка.
    /// Нужна потому, что база шардируется по имени: при изменении правил разбора
    /// исправленная запись ложится в новый файл, а прежняя остаётся сиротой.
    /// Источник истины — сохранённый заголовок раздачи (title).
    /// </summary>
    public sealed class FixAnimeToshoNamesMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "fixAnimeToshoNames";

        public FixAnimeToshoNamesMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        public object Run()
        {
            const string trackerName = "animetosho";
            int processed = 0, updated = 0, migrated = 0;

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    var toMigrate = new List<(string url, TorrentDetails t, string newKey)>();
                    bool touched = false;

                    foreach (var kv in fdb.Database.ToList())
                    {
                        var t = kv.Value;
                        if (t == null || !string.Equals(t.trackerName, trackerName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        processed++;

                        // Заголовок сохраняется целиком, поэтому имя можно пересобрать заново.
                        string source = !string.IsNullOrWhiteSpace(t.title) ? t.title : t.name;
                        if (string.IsNullOrWhiteSpace(source))
                            continue;

                        var parsed = AnimeToshoParser.ParseTitle(source);
                        string newName = parsed.Name?.Trim();
                        string newOriginalname = parsed.OriginalName?.Trim();

                        if (string.IsNullOrWhiteSpace(newName))
                            continue;
                        if (string.IsNullOrWhiteSpace(newOriginalname))
                            newOriginalname = newName;

                        if (newName == t.name && newOriginalname == t.originalname)
                            continue;

                        t.name = newName;
                        t.originalname = newOriginalname;
                        t._sn = StringConvert.SearchName(newName);
                        t._so = StringConvert.SearchName(newOriginalname);
                        if (parsed.Year > 0)
                            t.relased = parsed.Year;
                        updated++;
                        touched = true;

                        string newKey = FileDB.KeyForTorrent(t.name, t.originalname);
                        if (!string.IsNullOrEmpty(newKey) && newKey != item.Key && newKey.IndexOf(':') > 0)
                            toMigrate.Add((kv.Key, t, newKey));
                    }

                    foreach (var (url, t, newKey) in toMigrate)
                    {
                        fdb.Database.Remove(url);
                        FileDB.MigrateTorrentToNewKey(t, newKey);
                        migrated++;
                        touched = true;
                    }

                    if (fdb.Database.Count == 0)
                        FileDB.RemoveKeyFromMasterDb(item.Key);

                    if (touched)
                        fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            TryRebuildFastDb();

            return new { ok = true, processed, updated, migrated };
        }
    }
}
