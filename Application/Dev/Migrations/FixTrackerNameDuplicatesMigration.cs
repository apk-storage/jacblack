using System;
using System.Collections.Generic;
using System.Linq;
using JacBlack.Application.Index;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Infrastructure.Persistence;

namespace JacBlack.Application.Dev.Migrations
{
    /// <summary>
    /// Убирает повторы имён внутри поля <c>trackerName</c>:
    /// «bitru, kinozal, kinozal, rutor, bitru» → «bitru, kinozal, rutor».
    ///
    /// Откуда взялись. Склейка копий писала имя прямо в запись, которую ей дал
    /// путь чтения, — а это объект самой базы. Составное имя оставалось в ней
    /// навсегда, следующий поиск складывал его снова, и список рос. Сравнение
    /// при этом шло по строке целиком: «bitru, kinozal» не содержит подстроки
    /// «kinozal, rutor», поэтому вторая приписывалась вся. Замер 10.09.2026 на
    /// живом запросе по «Дюне»: 43 строки из 107 с повтором.
    ///
    /// Сам источник течи закрыт (склейка работает с клоном и складывает имена,
    /// а не строки), но накопленное в базе так и лежит: запись перепишется
    /// только когда обход снова её увидит. Эта миграция чистит разом.
    ///
    /// Ключ шарда считается по названию раздачи, а не по трекеру, поэтому
    /// переносить записи не требуется — правка чисто внутри шарда.
    ///
    /// Сначала запускать с dryRun=true — посчитает, ничего не трогая.
    /// </summary>
    public sealed class FixTrackerNameDuplicatesMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "fixTrackerNameDuplicates";

        public FixTrackerNameDuplicatesMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        public object Run() => Execute(dryRun: false);

        public object DryRun() => Execute(dryRun: true);

        object Execute(bool dryRun)
        {
            long scanned = 0, fixedRows = 0;
            var examples = new List<string>();

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

                    bool touched = false;

                    foreach (var kv in rows)
                    {
                        var t = kv.Value;
                        if (t == null || string.IsNullOrWhiteSpace(t.trackerName))
                            continue;

                        scanned++;

                        // Merge с пустой второй частью — это и есть нормализация:
                        // разобрать на имена, выбросить повторы, собрать обратно.
                        string clean = TrackerNames.Merge(t.trackerName, null);
                        if (string.Equals(clean, t.trackerName, StringComparison.Ordinal))
                            continue;

                        fixedRows++;

                        if (examples.Count < 10)
                            examples.Add($"{t.trackerName} → {clean}");

                        if (dryRun)
                            continue;

                        t.trackerName = clean;
                        touched = true;
                    }

                    if (touched)
                        shard.savechanges = true;
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

            return new { dryRun, scanned, fixedRows, examples };
        }
    }
}
