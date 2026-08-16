using System;
using System.Collections.Generic;
using System.Linq;

namespace JacBlack.Infrastructure.Parsing
{
    /// <summary>
    /// Имена трекеров у раздачи.
    ///
    /// Одна и та же раздача часто лежит на нескольких трекерах, и после склейки
    /// копий поле <c>trackerName</c> хранит их списком: «rutor, kinozal, bitru».
    /// Всё, что работает с трекером как с одним значением, на таких записях
    /// врёт: в статистике появлялось больше сотни «источников» вида
    /// «Rutor, bitru» с единичными раздачами, а настоящие трекеры недосчитывали
    /// своё; фильтр по трекеру такие раздачи не находил вовсе.
    /// </summary>
    public static class TrackerNames
    {
        static readonly char[] Разделители = { ',', ';' };

        /// <summary>Разбирает поле в список имён. Пустое поле даёт пустой список.</summary>
        public static IReadOnlyList<string> Split(string trackerName)
        {
            if (string.IsNullOrWhiteSpace(trackerName))
                return Array.Empty<string>();

            var части = trackerName.Split(Разделители, StringSplitOptions.RemoveEmptyEntries);
            if (части.Length == 1)
            {
                var одно = части[0].Trim();
                return одно.Length == 0 ? Array.Empty<string>() : new[] { одно };
            }

            var итог = new List<string>(части.Length);
            foreach (var часть in части)
            {
                var имя = часть.Trim();
                if (имя.Length == 0)
                    continue;
                // Одно и то же имя в списке дважды встречается: «Rutracker,
                // kinozal, rutracker». Для счётчиков это дало бы двойной учёт.
                if (!итог.Contains(имя, StringComparer.OrdinalIgnoreCase))
                    итог.Add(имя);
            }
            return итог;
        }

        /// <summary>Есть ли среди имён раздачи искомое (без учёта регистра).</summary>
        public static bool Contains(string trackerName, string искомое)
        {
            if (string.IsNullOrWhiteSpace(искомое))
                return false;
            foreach (var имя in Split(trackerName))
            {
                if (string.Equals(имя, искомое, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
