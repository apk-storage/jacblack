using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Application.Index;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;

namespace JacRed.Application.Dev.Migrations
{
    /// <summary>
    /// Убирает дубли AnimeTosho, оставшиеся от адресов со slug.
    ///
    /// Парсер сначала брал адрес из поля link — `animetosho.org/view/{slug}`.
    /// Slug на сайте меняется, поэтому запись задваивалась, а защита от дублей
    /// в GetTorrentIdFromUrl на таком адресе не работала. Теперь адрес строится
    /// из числового id; эта миграция удаляет прежние slug-записи там, где для
    /// той же раздачи уже есть запись с числовым адресом (сверка по magnet).
    /// </summary>
    public sealed class FixAnimeToshoUrlsMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "fixAnimeToshoUrls";

        public FixAnimeToshoUrlsMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        static bool IsNumericUrl(string url) =>
            !string.IsNullOrEmpty(url) &&
            System.Text.RegularExpressions.Regex.IsMatch(url, @"/view/\d+$");

        public object Run()
        {
            const string trackerName = "animetosho";
            int scanned = 0, removed = 0, keptSlug = 0;

            foreach (var item in FileDB.masterDb.ToArray())
            {
                using (var fdb = FileDB.OpenWrite(item.Key))
                {
                    if (fdb == null)
                        continue;

                    var rows = fdb.Database
                        .Where(kv => kv.Value != null &&
                                     string.Equals(kv.Value.trackerName, trackerName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (rows.Count == 0)
                        continue;

                    scanned += rows.Count;

                    // Раздачи с числовым адресом — эталон. Slug-двойник той же
                    // раздачи (тот же magnet) удаляем.
                    var numericMagnets = new HashSet<string>(
                        rows.Where(kv => IsNumericUrl(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value.magnet))
                            .Select(kv => kv.Value.magnet),
                        StringComparer.OrdinalIgnoreCase);

                    var toRemove = new List<string>();

                    foreach (var kv in rows)
                    {
                        if (IsNumericUrl(kv.Key))
                            continue;

                        if (!string.IsNullOrWhiteSpace(kv.Value.magnet) && numericMagnets.Contains(kv.Value.magnet))
                            toRemove.Add(kv.Key);
                        else
                            keptSlug++;   // двойника ещё нет — запись оставляем
                    }

                    if (toRemove.Count == 0)
                        continue;

                    foreach (var url in toRemove)
                    {
                        fdb.Database.Remove(url);
                        removed++;
                    }

                    if (fdb.Database.Count == 0)
                        FileDB.RemoveKeyFromMasterDb(item.Key);

                    fdb.savechanges = true;
                }
            }

            FileDB.SaveChangesToFile();
            TryRebuildFastDb();

            return new { ok = true, scanned, removed, keptSlug };
        }
    }
}
