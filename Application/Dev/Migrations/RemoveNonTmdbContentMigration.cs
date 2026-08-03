using System;
using System.Collections.Generic;
using System.Linq;
using JacBlack.Application.Index;
using JacBlack.Infrastructure.Persistence;

namespace JacBlack.Application.Dev.Migrations
{
    /// <summary>
    /// Убирает из базы то, чего нет в TMDB.
    ///
    /// Лампа работает с TMDB, поэтому спортивные трансляции до человека не
    /// доходят: место занимают, шарды при чтении раздувают, а в выдаче их
    /// нет. Замер 29.07.2026: около 28 500 записей, почти все с rutor.
    ///
    /// Новые такие записи уже не попадают — отбор стоит на входе в базу
    /// (FileDB.IsWantedContent). Эта миграция чистит накопленное.
    ///
    /// Сначала запускать с dryRun=true — посчитает, ничего не трогая.
    /// </summary>
    public sealed class RemoveNonTmdbContentMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "removeNonTmdbContent";

        public RemoveNonTmdbContentMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        public object Run() => Execute(dryRun: false);

        public object DryRun() => Execute(dryRun: true);

        object Execute(bool dryRun)
        {
            int scanned = 0, removed = 0, keysEmptied = 0;
            var byType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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

                    var toRemove = new List<string>();

                    foreach (var kv in rows)
                    {
                        var t = kv.Value;
                        if (t == null)
                            continue;

                        scanned++;

                        // Тот же признак, что и на входе: без типа не трогаем.
                        if (FileDB.IsWantedContent(t.types))
                            continue;

                        toRemove.Add(kv.Key);
                        removed++;

                        byTracker[t.trackerName ?? "?"] = byTracker.GetValueOrDefault(t.trackerName ?? "?") + 1;

                        foreach (string type in t.types ?? Array.Empty<string>())
                            byType[type] = byType.GetValueOrDefault(type) + 1;
                    }

                    if (toRemove.Count == 0 || dryRun)
                        continue;

                    foreach (string url in toRemove)
                        shard.Database.Remove(url);

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
                удалилиБы = dryRun ? removed : 0,
                удалено = dryRun ? 0 : removed,
                ключейОпустело = keysEmptied,
                поТипам = byType,
                поТрекерам = byTracker
            };
        }
    }
}
