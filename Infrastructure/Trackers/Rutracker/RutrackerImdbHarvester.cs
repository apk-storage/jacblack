using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Networking;
using JacBlack.Infrastructure.Persistence;

namespace JacBlack.Infrastructure.Trackers.Rutracker
{
    /// <summary>
    /// Достаёт код IMDB со страницы раздачи и запоминает его для карточки.
    ///
    /// Зачем. Код — единственное, чем можно развести тёзок: «Наследники»
    /// (Succession) и «Наследники» (Descendants) по названиям неотличимы,
    /// русское у обоих совпадает с карточкой дословно. Но в базе код редок:
    /// у «Наследников» он есть у 7 записей из 51, у «Пацанов» у 27 из 172 —
    /// его сообщают только англоязычные источники.
    ///
    /// Собирать код при обходе бесполезно: страницу раздачи мы читаем лишь
    /// для НОВЫХ записей, а их единицы за проход — замер 01.08.2026, обход
    /// из 3006 записей дал 30 новых и ноль прироста словаря.
    ///
    /// Поэтому берём иначе: код нужен ОДИН на карточку, а не на каждую
    /// раздачу. Сходить на любую её страницу достаточно один раз за всё
    /// время — дальше словарь отвечает сам. Делаем это в фоне, уже отдав
    /// ответ: страница читается через браузер и занимает секунды.
    /// </summary>
    public static class RutrackerImdbHarvester
    {
        static readonly ConcurrentDictionary<string, byte> _asked = new(StringComparer.OrdinalIgnoreCase);

        static readonly Regex ImdbLink = new Regex(@"imdb\.com/title/(tt\d{6,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Заказывает разовый поход за кодом карточки. Одну и ту же карточку
        /// спрашиваем единожды за время жизни службы: код не меняется, а
        /// браузерная сессия одна на всех и толкаться в ней нельзя.
        /// </summary>
        public static void EnsureInBackground(string topicUrl, string name, string originalname, int year)
        {
            if (string.IsNullOrWhiteSpace(topicUrl) || string.IsNullOrWhiteSpace(originalname))
                return;

            string key = $"{originalname}:{year}";
            if (!_asked.TryAdd(key, 0))
                return;

            // Уже знаем — ходить незачем.
            if (ImdbIndex.TryGetByTitle(originalname, year, out _))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    string html = await CloudflareClearance.FetchAsync(topicUrl);
                    if (string.IsNullOrEmpty(html))
                        return;

                    var m = ImdbLink.Match(html);
                    if (!m.Success)
                        return;

                    string imdb = m.Groups[1].Value.ToLowerInvariant();
                    ImdbIndex.Remember(imdb, name, originalname, year);
                    ImdbIndex.SaveIfDirty();

                    JacBlackLog.Information(JacBlackLogCategories.Trackers,
                        $"rutracker: код {imdb} запомнен для «{originalname}» ({year})");
                }
                catch (Exception ex)
                {
                    JacBlackLog.Swallowed(JacBlackLogCategories.Trackers, $"rutracker: код для «{originalname}» не добыт", ex);
                }
            });
        }
    }
}
