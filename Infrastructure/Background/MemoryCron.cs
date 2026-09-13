using System;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;

namespace JacBlack.Infrastructure.Background
{
    /// <summary>
    /// Присмотр за кучей больших объектов.
    ///
    /// Замер 13.09.2026: служба держала 2,9 ГБ при 598 МБ живых объектов —
    /// снимок кучи сразу после старта показал те же 546 МБ, то есть утечки
    /// нет. Разница почти вся в LOH: 794 МБ, из них 339 МБ дыр. Большие
    /// объекты уходят туда постоянно (страницы трекеров, JSON, массивы под
    /// разбор), а LOH по умолчанию не уплотняется — освобождённое место
    /// остаётся занятым для системы. Дважды это заканчивалось
    /// OutOfMemoryException и зависанием службы, 11 и 12 сентября.
    ///
    /// Уплотнение — блокирующая сборка второго поколения: на восьмистах
    /// мегабайтах она занимает секунды, а поиск у нас укладывается в 800 мс.
    /// Поэтому не по расписанию, а по трём условиям сразу: дыр больше порога
    /// в мегабайтах, они занимают заметную долю LOH, и с прошлого раза прошёл
    /// минимальный срок.
    /// </summary>
    public static class MemoryCron
    {
        /// <summary>Снимок кучи в понятных числах. Отдаётся и в лог, и в /stats/memory.</summary>
        public readonly struct Snapshot
        {
            public long WorkingSetBytes { get; init; }
            public long CommittedBytes { get; init; }
            public long HeapBytes { get; init; }
            public long Gen0Bytes { get; init; }
            public long Gen1Bytes { get; init; }
            public long Gen2Bytes { get; init; }
            public long LohBytes { get; init; }
            public long PohBytes { get; init; }
            public long LohFragmentedBytes { get; init; }
            public long TotalFragmentedBytes { get; init; }

            /// <summary>Доля дыр в LOH, в процентах. Пустой LOH — ноль, а не деление на ноль.</summary>
            public int LohFragmentedPercent =>
                LohBytes <= 0 ? 0 : (int)(LohFragmentedBytes * 100 / LohBytes);
        }

        /// <summary>Что дало последнее уплотнение. Для ручки диагностики.</summary>
        public static DateTime? LastCompactionAt { get; private set; }
        public static long LastCompactionFreedBytes { get; private set; }
        public static int LastCompactionMs { get; private set; }
        public static int CompactionCount { get; private set; }

        /// <summary>
        /// Считывает состояние кучи. Поколения приходят в порядке gen0, gen1,
        /// gen2, LOH, POH — берём по индексу с оглядкой на длину: на другой
        /// версии среды набор может быть короче, и падать из-за диагностики
        /// служба не должна.
        ///
        /// Читаем в лоб, без вспомогательных функций: GenerationInfo — это
        /// ReadOnlySpan, а span нельзя захватить ни в лямбду, ни в локальную
        /// функцию (CS8175).
        /// </summary>
        public static Snapshot Read()
        {
            var info = GC.GetGCMemoryInfo();
            var gens = info.GenerationInfo;

            return new Snapshot
            {
                WorkingSetBytes = Environment.WorkingSet,
                CommittedBytes = info.TotalCommittedBytes,
                HeapBytes = info.HeapSizeBytes,
                Gen0Bytes = gens.Length > 0 ? gens[0].SizeAfterBytes : 0,
                Gen1Bytes = gens.Length > 1 ? gens[1].SizeAfterBytes : 0,
                Gen2Bytes = gens.Length > 2 ? gens[2].SizeAfterBytes : 0,
                LohBytes = gens.Length > 3 ? gens[3].SizeAfterBytes : 0,
                PohBytes = gens.Length > 4 ? gens[4].SizeAfterBytes : 0,
                LohFragmentedBytes = gens.Length > 3 ? gens[3].FragmentationAfterBytes : 0,
                TotalFragmentedBytes = info.FragmentedBytes
            };
        }

        /// <summary>
        /// Уплотняет LOH и возвращает, сколько отдано системе. Вызывается и
        /// сторожем, и вручную из ручки диагностики — поэтому проверку условий
        /// держим снаружи, а здесь только сама работа.
        /// </summary>
        public static long Compact()
        {
            var before = MemoryCron.Read();
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Уплотняем один раз, вторым проходом лишь добираем то, что
            // освободили финализаторы. Второй проход намеренно БЕЗ компакции:
            // она стоит секунд, а повторять её ради остатков незачем.
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);

            watch.Stop();
            var after = MemoryCron.Read();

            long freed = before.CommittedBytes - after.CommittedBytes;
            LastCompactionAt = DateTime.UtcNow;
            LastCompactionFreedBytes = freed;
            LastCompactionMs = (int)watch.ElapsedMilliseconds;
            CompactionCount++;

            JacBlackLog.Warning(JacBlackLogCategories.Host,
                $"память: уплотнён LOH за {watch.ElapsedMilliseconds} мс, " +
                $"занято {Mb(before.CommittedBytes)} -> {Mb(after.CommittedBytes)} МБ, " +
                $"дыр в LOH {Mb(before.LohFragmentedBytes)} -> {Mb(after.LohFragmentedBytes)} МБ");

            return freed;
        }

        static long Mb(long bytes) => bytes / 1024 / 1024;

        /// <summary>
        /// Идёт ли сейчас тихий час. Время местное: сервер живёт в
        /// Europe/Zurich, аудитория — телевизоры в России, и ночь у нас общая.
        /// Окно, заданное «через полночь» (например, с 23 до 4), тоже работает.
        /// </summary>
        static bool ТихийЧас(Models.MemoryGuard conf)
        {
            int from = conf.quietFromHour;
            int to = conf.quietToHour;
            if (from == to)
                return true;   // окно не задано — значит ограничения по времени нет

            int hour = DateTime.Now.Hour;
            return from < to
                ? hour >= from && hour < to
                : hour >= from || hour < to;
        }

        async public static Task Run(CancellationToken cancellationToken = default)
        {
            // Даём службе подняться и прогреть индексы: на старте куча растёт
            // сама по себе, и мерить её в это время бессмысленно.
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var conf = AppInit.conf?.memoryGuard;
                int wait = conf == null || conf.checkMinutes < 1 ? 10 : conf.checkMinutes;
                await Task.Delay(TimeSpan.FromMinutes(wait), cancellationToken);

                try
                {
                    conf = AppInit.conf?.memoryGuard;
                    if (conf == null || !conf.enable)
                        continue;

                    var now = MemoryCron.Read();

                    bool многоДыр = Mb(now.LohFragmentedBytes) >= conf.fragmentedMb;
                    bool заметнаяДоля = now.LohFragmentedPercent >= conf.fragmentedPercent;
                    bool срокВышел = LastCompactionAt == null ||
                                     (DateTime.UtcNow - LastCompactionAt.Value).TotalMinutes >= conf.minIntervalMinutes;

                    if (многоДыр && заметнаяДоля && срокВышел && ТихийЧас(conf))
                        Compact();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Присмотр за памятью не должен ронять службу: он полезен,
                    // но не обязателен.
                    JacBlackLog.Swallowed(JacBlackLogCategories.Host, "присмотр за памятью", ex);
                }
            }
        }
    }
}
