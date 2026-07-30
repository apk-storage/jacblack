using System;
using System.Collections.Generic;
using System.Linq;
using JacRed.Application.Index;
using JacRed.Infrastructure.Persistence;

namespace JacRed.Application.Dev.Migrations
{
    /// <summary>
    /// Проставляет код IMDB раздачам, у которых его нет, по совпадению
    /// оригинального названия и года.
    ///
    /// Откуда берётся код. Его сообщают три источника из двадцати — eztv, yts и
    /// piratebay. Но фильм-то один и тот же: если yts принёс «Interstellar 2014
    /// → tt0816692», то русская раздача с тем же оригинальным названием и годом
    /// — про него же. Так поиск по коду начинает находить и русские раздачи.
    ///
    /// Почему год обязателен. Без него «Дюна» 1984 года склеилась бы с «Дюной»
    /// 2021-го. Название приводится тем же способом, что и ключи базы — без
    /// пробелов, знаков и регистра.
    ///
    /// Новые записи получают код сразу при сохранении; эта миграция нужна для
    /// уже накопленного. Запускать имеет смысл после того, как словарь наполнен
    /// обходом eztv и yts.
    ///
    /// Сначала запускать с dryRun=true.
    /// </summary>
    public sealed class FillImdbFromDictionaryMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "fillImdbFromDictionary";

        public FillImdbFromDictionaryMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        public object Run() => Execute(dryRun: false);

        public object DryRun() => Execute(dryRun: true);

        object Execute(bool dryRun)
        {
            long scanned = 0, alreadyHad = 0, filled = 0, noYear = 0, notFound = 0;
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

                    bool touched = false;

                    foreach (var kv in rows)
                    {
                        var t = kv.Value;
                        if (t == null)
                            continue;

                        scanned++;

                        if (!string.IsNullOrWhiteSpace(t.imdb))
                        {
                            alreadyHad++;
                            continue;
                        }

                        if (t.relased <= 1900)
                        {
                            noYear++;
                            continue;
                        }

                        if (!ImdbIndex.TryGetByTitle(t.originalname, t.relased, out string code)
                            && !ImdbIndex.TryGetByTitle(t.name, t.relased, out code))
                        {
                            notFound++;
                            continue;
                        }

                        filled++;
                        byTracker[t.trackerName ?? "?"] = byTracker.GetValueOrDefault(t.trackerName ?? "?") + 1;

                        if (dryRun)
                            continue;

                        t.imdb = code;
                        touched = true;
                    }

                    if (!dryRun && touched)
                        shard.savechanges = true;
                }
                finally
                {
                    shard?.Dispose();
                }
            }

            if (!dryRun)
                FileDB.SaveChangesToFile();

            return new
            {
                ok = true,
                dryRun,
                просмотрено = scanned,
                кодУжеБыл = alreadyHad,
                проставлено = filled,
                безГода = noYear,
                несовпало = notFound,
                кодовВСловаре = ImdbIndex.Count,
                поТрекерам = byTracker
            };
        }
    }
}
