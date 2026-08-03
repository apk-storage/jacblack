using System.Globalization;
using System.Text.RegularExpressions;

namespace JacBlack.Infrastructure.Parsing
{
    /// <summary>
    /// Разбор человеческой строки размера («5.09 ГБ», «700 MB») в байты.
    ///
    /// Было тремя независимыми реализациями: в FileDB, в обслуживании базы и в
    /// выдаче Torznab. Две из них требовали между числом и единицей ОБЫЧНЫЙ
    /// пробел — а toloka отдаёт неразрывный, и размер у неё не разбирался вовсе.
    /// Замер 30.07.2026: 45% записей toloka в базе с нулевым размером, у всех
    /// остальных трекеров таких нет.
    ///
    /// Здесь одна реализация на всех, и она принимает любой пробел.
    /// </summary>
    public static class SizeParser
    {
        const long Kb = 1024;
        const long Mb = 1024 * Kb;
        const long Gb = 1024 * Mb;
        const long Tb = 1024 * Gb;

        static readonly Regex Pattern = new Regex(
            @"([0-9]+(?:[.,][0-9]+)?)\s*(KB|КБ|MB|МБ|GB|ГБ|TB|ТБ|B|Б)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Возвращает 0, если разобрать не удалось.</summary>
        public static long ToBytes(string sizeName)
        {
            if (string.IsNullOrWhiteSpace(sizeName))
                return 0;

            var m = Pattern.Match(sizeName);
            if (!m.Success)
                return 0;

            if (!double.TryParse(m.Groups[1].Value.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double value) || value <= 0)
                return 0;

            // Без единицы считаем мегабайтами: так вели себя обе реализации,
            // работавшие с базой, и такие значения там уже лежат.
            return m.Groups[2].Value.ToLowerInvariant() switch
            {
                "kb" or "кб" => (long)(value * Kb),
                "gb" or "гб" => (long)(value * Gb),
                "tb" or "тб" => (long)(value * Tb),
                "b" or "б" => (long)value,
                _ => (long)(value * Mb)
            };
        }
    }
}
