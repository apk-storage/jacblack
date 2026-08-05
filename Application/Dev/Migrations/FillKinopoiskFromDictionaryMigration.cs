using System;
using System.Collections.Generic;
using System.Linq;
using JacBlack.Application.Index;
using JacBlack.Infrastructure.Persistence;

namespace JacBlack.Application.Dev.Migrations
{
    /// <summary>
    /// Проставляет код Кинопоиска раздачам, у которых его нет, по совпадению
    /// названия и года. Близнец <see cref="FillImdbFromDictionaryMigration"/>.
    ///
    /// Откуда берётся код. Его приносит один источник — kinozal, со страницы
    /// раздачи. Но фильм один и тот же: если код добыт для «Русской жены»
    /// 2024 года, то и раздачи того же фильма на bitru и rutracker — про него
    /// же. Так один поход за страницей закрывает всю карточку.
    ///
    /// Почему спрашиваем оба названия. У русского кино оригинальное название
    /// совпадает с русским, и словарь может знать вещь под любым из них.
    ///
    /// Почему год обязателен. Без него «Ирония судьбы» 1975 года склеилась бы
    /// с продолжением 2007-го.
    ///
    /// Новые записи получают код сразу при сохранении; эта миграция нужна для
    /// уже накопленного. Запускать имеет смысл после того, как словарь
    /// наполнился походами за карточками.
    ///
    /// Сначала запускать с dryRun=true.
    /// </summary>
    public sealed class FillKinopoiskFromDictionaryMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "fillKinopoiskFromDictionary";

        public FillKinopoiskFromDictionaryMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

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

                        if (!string.IsNullOrWhiteSpace(t.kinopoisk))
                        {
                            alreadyHad++;
                            continue;
                        }

                        if (t.relased <= 1900)
                        {
                            noYear++;
                            continue;
                        }

                        if (!KinopoiskIndex.TryGetByTitle(t.originalname, t.relased, out string code)
                            && !KinopoiskIndex.TryGetByTitle(t.name, t.relased, out code))
                        {
                            notFound++;
                            continue;
                        }

                        filled++;
                        byTracker[t.trackerName ?? "?"] = byTracker.GetValueOrDefault(t.trackerName ?? "?") + 1;

                        if (dryRun)
                            continue;

                        t.kinopoisk = code;
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
                кодовВСловаре = KinopoiskIndex.Count,
                поТрекерам = byTracker
            };
        }
    }
}
