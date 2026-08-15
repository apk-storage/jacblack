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
    /// Год почти всегда стоит в самом заголовке — его и берём. А там, где его
    /// нет вовсе (сценовые релизы вроде «King.and.Maxwell.S01E01.HDTV.x264-SM»),
    /// берём из словаря по коду IMDB: 15.08.2026 таких записей было 165 411,
    /// у 32 044 из них есть код, и для 28 679 год в словаре нашёлся.
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
            long scanned = 0, hadYear = 0, fixedUp = 0, rangeSkipped = 0, notFound = 0, fromCode = 0;
            var byTracker = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var samples = new List<string>();
            var codeSamples = new List<string>();

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
                            // Года в заголовке нет — пробуем словарь по коду.
                            //
                            // Так живут сценовые релизы: «King.and.Maxwell.S01E01
                            // .EXTENDED.HDTV.x264-SM» года не пишет вовсе, и
                            // такие записи не попадали ни в одну карточку.
                            // Замер 15.08.2026: без года 165 411 раздач, у
                            // 32 044 из них есть код, и для 28 679 год лежит
                            // в словаре.
                            //
                            // У сериала это год ПЕРВОГО выхода, а не сезона —
                            // и это верно: карточка сериала в Лампе одна, с
                            // годом начала, и запрос приходит именно с ним.
                            if (!string.IsNullOrWhiteSpace(t.imdb)
                                && ImdbIndex.TryGet(t.imdb, out var known)
                                && known?.Year > 1900)
                            {
                                fromCode++;
                                byTracker[t.trackerName ?? "?"] = byTracker.GetValueOrDefault(t.trackerName ?? "?") + 1;

                                if (codeSamples.Count < 10)
                                    codeSamples.Add($"{known.Year} ← {(t.title ?? "").Substring(0, Math.Min(80, (t.title ?? "").Length))}");

                                if (!dryRun)
                                {
                                    t.relased = known.Year;
                                    touched = true;
                                }
                                continue;
                            }

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
                восстановленоИзЗаголовка = fixedUp,
                восстановленоПоКоду = fromCode,
                пропущеноДиапазонов = rangeSkipped,
                годаВЗаголовкеНет = notFound,
                поТрекерам = byTracker,
                примеры = samples,
                примерыПоКоду = codeSamples
            };
        }
    }
}
