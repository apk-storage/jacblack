using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace JacRed.Infrastructure.Indexers
{
    /// <summary>Что известно про один глубокий обход.</summary>
    public class CrawlRun
    {
        public DateTime StartedAt { get; set; }

        /// <summary>null, пока обход идёт.</summary>
        public DateTime? FinishedAt { get; set; }

        /// <summary>Чем кончился: «ok», «прерван», «отменён».</summary>
        public string Outcome { get; set; }
    }

    /// <summary>
    /// Ход глубоких обходов: что идёт прямо сейчас и чем кончилось прошлое.
    ///
    /// Зачем отдельно от логов. Положение в очереди лежит в файлах и читается
    /// с диска, а вот «идёт ли обход прямо сейчас» не лежит нигде: это признак
    /// занятости в памяти службы. Со стороны отличить «обход работает восемь
    /// часов» от «обход не запускался» было нельзя — приходилось идти по ssh и
    /// сопоставлять журналы.
    ///
    /// Живёт в памяти намеренно: после перезапуска ни один обход не идёт, и
    /// показывать прошлые как текущие было бы враньём. Прошлый исход теряется,
    /// но он и так виден в журнале парсера.
    ///
    /// Пишется в единственной точке — <see cref="TrackerSyncHelpers"/>, через
    /// которую проходят все глубокие обходы без исключения.
    /// </summary>
    public static class CrawlProgress
    {
        static readonly ConcurrentDictionary<string, CrawlRun> _runs =
            new ConcurrentDictionary<string, CrawlRun>(StringComparer.OrdinalIgnoreCase);

        public static void Begin(string tracker)
        {
            if (string.IsNullOrWhiteSpace(tracker))
                return;

            _runs[tracker] = new CrawlRun { StartedAt = DateTime.UtcNow };
        }

        public static void End(string tracker, string outcome)
        {
            if (string.IsNullOrWhiteSpace(tracker) || !_runs.TryGetValue(tracker, out var run))
                return;

            run.FinishedAt = DateTime.UtcNow;
            run.Outcome = outcome;
        }

        public static IReadOnlyDictionary<string, CrawlRun> Snapshot()
            => new Dictionary<string, CrawlRun>(_runs, StringComparer.OrdinalIgnoreCase);
    }
}
