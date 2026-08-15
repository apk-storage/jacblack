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
    /// С 15.08.2026 чистит ещё и не-видео: книги, музыку, картинки, шаблоны и
    /// софт. Отбор по типу их не ловил — у nnmclub раздел назван словами,
    /// книжные разделы мы не опознаём, и работало правило «неопознанное
    /// считаем кино», то есть книга приходила как movie. Замер: 32 435 таких
    /// записей, 1.65% базы и 14% всего nnmclub.
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
            var samples = new List<string>();

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

                        // Те же два признака, что и на входе в базу.
                        //
                        // Первый — тип: спорт и прочее, чего нет в TMDB.
                        // Второй — сама раздача: книги, музыка, картинки и
                        // софт приходят с типом movie, потому что раздел у
                        // nnmclub назван словами и книжные мы не опознаём.
                        bool nonVideo = Infrastructure.Parsing.NonVideoContent.IsNotVideo(t.title);

                        if (FileDB.IsWantedContent(t.types) && !nonVideo)
                            continue;

                        toRemove.Add(kv.Key);
                        removed++;

                        byTracker[t.trackerName ?? "?"] = byTracker.GetValueOrDefault(t.trackerName ?? "?") + 1;

                        if (nonVideo)
                            byType["не видео"] = byType.GetValueOrDefault("не видео") + 1;

                        foreach (string type in t.types ?? Array.Empty<string>())
                            byType[type] = byType.GetValueOrDefault(type) + 1;

                        if (samples.Count < 20)
                            samples.Add($"[{t.trackerName}] {(t.title ?? "").Substring(0, Math.Min(80, (t.title ?? "").Length))}");
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
                поТрекерам = byTracker,
                примеры = samples
            };
        }
    }
}
