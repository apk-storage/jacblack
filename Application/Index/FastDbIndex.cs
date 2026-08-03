using System;
using System.Collections.Generic;
using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Persistence;

namespace JacBlack.Application.Index
{
    public class FastDbIndex : IFastDbIndex
    {
        /// <summary>Singleton instance registered as <see cref="IFastDbIndex"/> in Program.</summary>
        public static FastDbIndex Default { get; } = new FastDbIndex();

        Dictionary<string, List<string>> _fastdb;

        /// <summary>
        /// Снимок ключей базы обычным массивом.
        ///
        /// Нечёткий поиск ищет подстроку и по словарю ускориться не может —
        /// ему нужен перебор. Но перебирать ConcurrentDictionary через LINQ
        /// дорого: замер 29.07.2026 показал 157 мс на 296 780 ключей, тогда
        /// как побайтовый поиск подстроки в массиве строк укладывается в
        /// десятки миллисекунд. Массив пересобирается вместе с индексом.
        /// </summary>
        string[] _keys = Array.Empty<string>();

        readonly object _lock = new object();

        public Dictionary<string, List<string>> Get(bool update = false)
        {
            if (_fastdb != null && !update)
                return _fastdb;

            lock (_lock)
            {
                if (_fastdb != null && !update)
                    return _fastdb;

                if (update)
                    JacBlackLog.Information("fastdb", $"rebuild start / {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                var fastdb = new Dictionary<string, List<string>>();

                foreach (var item in FileDB.masterDb.ToArray())
                {
                    foreach (string k in item.Key.Split(":"))
                    {
                        if (string.IsNullOrEmpty(k))
                            continue;

                        if (fastdb.TryGetValue(k, out List<string> keys))
                            keys.Add(item.Key);
                        else
                            fastdb.Add(k, new List<string>() { item.Key });
                    }
                }

                _fastdb = fastdb;

                if (update)
                    JacBlackLog.Information("fastdb", $"rebuild end / {DateTime.Now:yyyy-MM-dd HH:mm:ss} keys={fastdb.Count}");
            }

            return _fastdb;
        }

        public void Rebuild() => Get(update: true);
    }
}
