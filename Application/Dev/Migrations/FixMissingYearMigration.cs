using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacBlack.Application.Index;
using JacBlack.Infrastructure.Persistence;

namespace JacBlack.Application.Dev.Migrations
{
    /// <summary>
    /// Восстанавливает год у записей, где разбор его потерял.
    ///
    /// Зачем. С 06.08.2026 год в запросе — жёсткое условие отбора: раздача
    /// с неразобранным годом в карточку с годом не попадает. Это правильно
    /// (иначе сериалы 1968 года лезут в карточку фильма 2026-го), но платят
    /// за это и честные записи, у которых год не разобрался по нашей же вине.
    /// Замер до правки: у «Матрицы» 22 такие раздачи из 131, у «Игры
    /// престолов» — 58 из 552.
    ///
    /// Год почти всегда стоит в самом заголовке — его и берём.
    ///
    /// Осторожность с диапазонами. «The Matrix: Trilogy (1999-2003)» — это
    /// сборник РАЗНЫХ фильмов, и приписывать ему 1999-й значит вернуть его
    /// в карточку первого фильма. Поэтому у фильмов диапазон не берём вовсе,
    /// только одиночный год. У сериалов наоборот: «[2011-2019]» — это один
    /// сериал, идущий годами, и первый год ему подходит.
    ///
    /// Сначала запускать с dryRun=true.
    /// </summary>
    public sealed class FixMissingYearMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "fixMissingYear";

        public FixMissingYearMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        public object Run() => Execute(dryRun: false);

        public object DryRun() => Execute(dryRun: true);

        // Год в круглых или квадратных скобках, либо между косыми чертами —
        // три написания покрывают все наши трекеры.
        static readonly Regex YearAny = new Regex(
            @"[\(\[/]\s*(1[89]\d{2}|20\d{2})\s*(?<range>[-–—,]\s*(1[89]\d{2}|20\d{2}))?\s*[,\)\]/]",
            RegexOptions.Compiled);

        static readonly string[] SerialTypes = { "serial", "multserial", "docuserial", "tvshow" };

        object Execute(bool dryRun)
        {
            long scanned = 0, hadYear = 0, fixedUp = 0, rangeSkipped = 0, notFound = 0;
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

                    bool touched = false;

                    foreach (var kv in rows)
                    {
                        var t = kv.Value;
                        if (t == null)
                            continue;

                        scanned++;

                        if (t.relased > 1900)
                        {
                            hadYear++;
                            continue;
                        }

                        var m = YearAny.Match(t.title ?? "");
                        if (!m.Success)
                        {
                            notFound++;
                            continue;
                        }

                        bool serial = t.types != null && t.types.Any(x => SerialTypes.Contains(x));

                        // Диапазон у фильма — почти всегда сборник разных
                        // вещей. Оставляем без года: пусть лучше не найдётся,
                        // чем встанет в чужую карточку.
                        if (m.Groups["range"].Success && !serial)
                        {
                            rangeSkipped++;
                            continue;
                        }

                        if (!int.TryParse(m.Groups[1].Value, out int year) || year <= 1900)
                        {
                            notFound++;
                            continue;
                        }

                        fixedUp++;
                        byTracker[t.trackerName ?? "?"] = byTracker.GetValueOrDefault(t.trackerName ?? "?") + 1;

                        if (samples.Count < 15)
                            samples.Add($"{year} ← {(t.title ?? "").Substring(0, Math.Min(90, (t.title ?? "").Length))}");

                        if (dryRun)
                            continue;

                        t.relased = year;
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
                годБыл = hadYear,
                восстановлено = fixedUp,
                пропущеноДиапазонов = rangeSkipped,
                годаВЗаголовкеНет = notFound,
                поТрекерам = byTracker,
                примеры = samples
            };
        }
    }
}
