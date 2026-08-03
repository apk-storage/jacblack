using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace JacBlack.Infrastructure.Parsing
{
    /// <summary>
    /// Готовые скомпилированные регулярки по шаблону.
    ///
    /// В разборе страниц принято писать `new Regex(шаблон, IgnoreCase).Match(строка)`
    /// прямо в теле цикла — то есть шаблон разбирается заново на каждое поле
    /// каждой строки. На странице rutor это 100 строк по полтора десятка полей:
    /// полторы тысячи разборов одних и тех же шаблонов за страницу.
    ///
    /// Статический кеш .NET (`Regex.CacheSize`) сюда не помогает вовсе: он
    /// работает только для статических вызовов `Regex.Match(строка, шаблон)`,
    /// а созданный вручную объект мимо него проходит.
    ///
    /// Здесь шаблон компилируется один раз на весь срок жизни приложения.
    /// Compiled оправдан именно потому, что шаблоны переиспользуются тысячи раз.
    /// </summary>
    public static class RegexCache
    {
        static readonly ConcurrentDictionary<(string pattern, RegexOptions options), Regex> _cache = new();

        public static Regex Get(string pattern, RegexOptions options = RegexOptions.None)
        {
            return _cache.GetOrAdd((pattern, options),
                key => new Regex(key.pattern, key.options | RegexOptions.Compiled));
        }

        public static Match Match(string input, string pattern, RegexOptions options = RegexOptions.None)
            => Get(pattern, options).Match(input ?? string.Empty);

        public static bool IsMatch(string input, string pattern, RegexOptions options = RegexOptions.None)
            => Get(pattern, options).IsMatch(input ?? string.Empty);

        public static string Replace(string input, string pattern, string replacement, RegexOptions options = RegexOptions.None)
            => Get(pattern, options).Replace(input ?? string.Empty, replacement);

        public static MatchCollection Matches(string input, string pattern, RegexOptions options = RegexOptions.None)
            => Get(pattern, options).Matches(input ?? string.Empty);

        /// <summary>Сколько разных шаблонов уже скомпилировано — для диагностики.</summary>
        public static int Count => _cache.Count;
    }
}
