using System;
using System.Collections.Concurrent;
using System.Threading;

namespace JacBlack.Infrastructure.Parsing
{
    /// <summary>
    /// Счётчики одного прогона парсера. Считаются НЕ в самих парсерах, а в месте,
    /// через которое проходят все записи без исключения — в FileDB. Причина: из
    /// семнадцати парсеров поштучный лог ведут только шесть, а крупные (Rutor,
    /// Rutracker, Kinozal, NNMClub) не считают ничего. Привязка идёт по имени
    /// трекера в самой записи, поэтому одновременные прогоны разных трекеров
    /// не путаются между собой.
    /// </summary>
    public static class ParseCounters
    {
        public sealed class Counters
        {
            int _added;
            int _updated;
            public DateTime StartedAt { get; } = DateTime.UtcNow;

            public int Added => _added;
            public int Updated => _updated;

            public void IncAdded() => Interlocked.Increment(ref _added);
            public void IncUpdated() => Interlocked.Increment(ref _updated);
        }

        static readonly ConcurrentDictionary<string, Counters> _active =
            new ConcurrentDictionary<string, Counters>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Открыть счёт для трекера. Повторный вызов обнуляет.</summary>
        public static void Begin(string trackerName)
        {
            if (string.IsNullOrWhiteSpace(trackerName))
                return;
            _active[trackerName] = new Counters();
        }

        /// <summary>Забрать итог и закрыть счёт. null, если счёт не открывали.</summary>
        public static Counters End(string trackerName)
        {
            if (string.IsNullOrWhiteSpace(trackerName))
                return null;
            return _active.TryRemove(trackerName, out var c) ? c : null;
        }

        /// <summary>Отметить добавленную запись. Вызывается из FileDB.</summary>
        public static void Added(string trackerName)
        {
            if (!string.IsNullOrWhiteSpace(trackerName) && _active.TryGetValue(trackerName, out var c))
                c.IncAdded();
        }

        /// <summary>Отметить обновлённую запись. Вызывается из FileDB.</summary>
        public static void Updated(string trackerName)
        {
            if (!string.IsNullOrWhiteSpace(trackerName) && _active.TryGetValue(trackerName, out var c))
                c.IncUpdated();
        }
    }
}
