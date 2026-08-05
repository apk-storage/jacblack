using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Persistence;

namespace JacBlack.Infrastructure.Trackers.Kinozal
{
    /// <summary>
    /// Достаёт код Кинопоиска со страницы раздачи kinozal и запоминает его
    /// для карточки. Близнец <c>RutrackerImdbHarvester</c>, но для русского
    /// кино.
    ///
    /// Зачем. Тёзок разводит только код. У зарубежных вещей эту работу делает
    /// IMDB, а у русских его нет ни у кого — ни трекеры не сообщают, ни Лампа
    /// в торрент-поиск не шлёт. Кинопоиск — единственный доступный аналог, и
    /// ссылку на него публикует kinozal.
    ///
    /// Почему не при обходе. Страницу раздачи массовый обход не читает вовсе:
    /// он берёт списки и добирает хеш отдельным запросом. Ходить за каждой
    /// страницей — это тысячи лишних запросов к трекеру, который ограничивает
    /// частоту.
    ///
    /// Поэтому берём как с rutracker: код нужен ОДИН на карточку, а не на
    /// каждую раздачу. Сходили один раз за всё время — дальше словарь отвечает
    /// сам. Делаем в фоне, уже отдав ответ клиенту.
    /// </summary>
    public static class KinozalKinopoiskHarvester
    {
        /// <summary>
        /// Карточки, за которыми уже ходили. Одну и ту же спрашиваем единожды
        /// за время жизни службы: код не меняется, а бюджет запросов к трекеру
        /// надо беречь.
        /// </summary>
        static readonly ConcurrentDictionary<string, byte> _asked = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Сколько походов держим одновременно. Единица не случайна: у kinozal
        /// одна сессия на всех, и толкаться в ней нельзя — на rutracker-сборщике
        /// это уже проходили.
        /// </summary>
        static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Заказывает разовый поход за кодом карточки.
        ///
        /// <paramref name="kinozalId"/> — номер раздачи на kinozal, только он
        /// открывает страницу с ссылкой на Кинопоиск.
        /// </summary>
        public static void EnsureInBackground(
            KinozalSyncService kinozal, string kinozalId, string name, string originalname, int year)
        {
            if (kinozal == null || string.IsNullOrWhiteSpace(kinozalId))
                return;

            // Ключ по названию, а не по номеру раздачи: раздач у карточки
            // десятки, а спросить надо один раз.
            string title = !string.IsNullOrWhiteSpace(originalname) ? originalname : name;
            if (string.IsNullOrWhiteSpace(title) || year <= 1900)
                return;

            string key = $"{title}:{year}";
            if (!_asked.TryAdd(key, 0))
                return;

            // Уже знаем — ходить незачем.
            if (KinopoiskIndex.TryGetByTitle(title, year, out _))
                return;

            _ = Task.Run(async () =>
            {
                await _gate.WaitAsync();
                try
                {
                    // Пока стояли в очереди, код мог принести кто-то другой.
                    if (KinopoiskIndex.TryGetByTitle(title, year, out _))
                        return;

                    string code = await kinozal.GetKinopoiskIdAsync(kinozalId);
                    if (string.IsNullOrWhiteSpace(code))
                        return;

                    KinopoiskIndex.Remember(code, name, originalname, year);

                    // Сохраняем немедленно, а не по общему расписанию. Обычная
                    // отсрочка бережёт диск при потоке записей, но здесь поток
                    // редкий — один поход на карточку за всё время. Зато цена
                    // потери высокая: перезапуск в ближайшие минуты стирает
                    // добытое, и за тем же кодом придётся идти на трекер снова.
                    // На этом уже попались: из двух добытых кодов пережил
                    // перезапуск один.
                    KinopoiskIndex.SaveIfDirty(force: true);

                    JacBlackLog.Information(JacBlackLogCategories.Trackers,
                        $"kinozal: код Кинопоиска {code} запомнен для «{title}» ({year})");
                }
                catch (Exception ex)
                {
                    JacBlackLog.Swallowed(JacBlackLogCategories.Trackers,
                        $"kinozal: код Кинопоиска для «{title}» не добыт", ex);
                }
                finally
                {
                    _gate.Release();
                }
            });
        }
    }
}
