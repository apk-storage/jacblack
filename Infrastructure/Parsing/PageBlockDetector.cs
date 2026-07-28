using System;
using System.Collections.Concurrent;
using JacRed.Infrastructure.Logging;

namespace JacRed.Infrastructure.Parsing
{
    /// <summary>
    /// Узнаёт страницу-заглушку: капчу, требование входа, вызов Cloudflare.
    ///
    /// Понадобилось после torrent.by: он отдавал HTTP 200 со страницей
    /// «С вашего IP адреса поступают подозрительные запросы», парсер честно
    /// не находил там раздач и писал «добавлено=0». Со стороны это выглядело
    /// как здоровый трекер, у которого просто нет нового — и так почти год.
    ///
    /// Поведение не меняется: страница возвращается как есть, добавляется
    /// только запись в лог. Признаки нарочно узкие, чтобы не принять за
    /// блокировку обычную раздачу со словом «captcha» в названии.
    /// </summary>
    public static class PageBlockDetector
    {
        static readonly (string marker, string reason)[] Markers =
        {
            ("Введите проверочный код",       "капча: трекер счёл наши запросы подозрительными"),
            ("подозрительные запросы",         "капча: трекер счёл наши запросы подозрительными"),
            ("Just a moment...",               "проверка Cloudflare"),
            ("Checking your browser before",   "проверка Cloudflare"),
            ("Доступ ограничен",               "доступ ограничен провайдером или трекером"),
            ("Вы исчерпали лимит",             "исчерпан лимит запросов"),
            ("Слишком много запросов",         "исчерпан лимит запросов")
        };

        // Один и тот же трекер отдаёт заглушку на каждой странице подряд,
        // поэтому про хост говорим не чаще раза в час.
        static readonly ConcurrentDictionary<string, DateTime> _lastReport = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Похоже ли, что вместо содержимого пришла заглушка.</summary>
        public static bool LooksBlocked(string html, out string reason)
        {
            reason = null;

            // Настоящие страницы трекеров крупные; заглушки — около килобайта.
            if (string.IsNullOrEmpty(html) || html.Length > 20_000)
                return false;

            foreach (var (marker, why) in Markers)
            {
                if (html.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    reason = why;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Пишет в лог, если страница оказалась заглушкой. Не чаще раза в час на хост.</summary>
        public static void ReportIfBlocked(string host, string url, string html)
        {
            if (string.IsNullOrWhiteSpace(host) || !LooksBlocked(html, out string reason))
                return;

            var now = DateTime.UtcNow;
            if (_lastReport.TryGetValue(host, out var last) && now < last.AddHours(1))
                return;

            _lastReport[host] = now;

            JacRedLog.Warning(JacRedLogCategories.Parser,
                $"{host} отдал заглушку вместо содержимого — {reason}. Обход будет находить ноль раздач, пока это не снимут. Адрес: {url}");
        }
    }
}
