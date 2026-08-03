using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;

namespace JacBlack.Infrastructure.Networking
{
    /// <summary>
    /// Слушает просьбу трекера сбавить обороты.
    ///
    /// Зачем. Раньше ответ 429 ничем не отличался от любой другой неудачи:
    /// страница считалась непрочитанной, и обход шёл дальше с той же
    /// частотой. Банят обычно не за скорость саму по себе, а именно за то,
    /// что на явную просьбу «подожди» никто не реагирует.
    ///
    /// Устройство простое: у каждого хоста своя отметка «до этого времени
    /// не беспокоить». Пауза берётся из заголовка Retry-After, а если его
    /// нет — нарастающая, с потолком, чтобы одна неудача не выключила
    /// трекер на полдня. Ожидание касается ТОЛЬКО своего хоста: остальные
    /// трекеры в это время работают как ни в чём не бывало.
    /// </summary>
    public static class HostThrottle
    {
        sealed class State
        {
            public DateTime Until;
            public int Strikes;
        }

        static readonly ConcurrentDictionary<string, State> _hosts =
            new ConcurrentDictionary<string, State>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Пауза без подсказки: удваивается с каждым отказом подряд.</summary>
        static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Потолок паузы. Дольше ждать бессмысленно: глубокий обход всё равно
        /// закончится по своему сроку, а трекер к следующему прогону остынет.
        /// </summary>
        static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(10);

        /// <summary>Сколько ещё ждать по этому хосту. Ноль — можно идти.</summary>
        public static TimeSpan Remaining(string host)
        {
            if (string.IsNullOrWhiteSpace(host) || !_hosts.TryGetValue(host, out var state))
                return TimeSpan.Zero;

            var left = state.Until - DateTime.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        /// <summary>
        /// Ждёт, если хост попросил паузу. Возвращает false, если ждать
        /// пришлось бы дольше отпущенного вызывающему времени — тогда честнее
        /// пропустить страницу, чем висеть.
        /// </summary>
        public static async Task<bool> WaitAsync(string host, TimeSpan budget, CancellationToken ct = default)
        {
            var left = Remaining(host);
            if (left <= TimeSpan.Zero)
                return true;

            if (left > budget)
                return false;

            await Task.Delay(left, ct);
            return true;
        }

        /// <summary>Просили ли нас сбавить обороты.</summary>
        public static bool IsThrottleResponse(HttpResponseMessage response) =>
            response != null && (int)response.StatusCode == 429;

        /// <summary>
        /// Запомнить просьбу. Пауза — из Retry-After, иначе нарастающая.
        /// </summary>
        public static void Throttled(string host, HttpResponseMessage response)
        {
            if (string.IsNullOrWhiteSpace(host))
                return;

            var state = _hosts.GetOrAdd(host, _ => new State());

            lock (state)
            {
                state.Strikes = Math.Min(state.Strikes + 1, 8);

                TimeSpan delay = FromRetryAfter(response)
                    ?? TimeSpan.FromSeconds(BaseDelay.TotalSeconds * Math.Pow(2, state.Strikes - 1));

                if (delay > MaxDelay)
                    delay = MaxDelay;

                var until = DateTime.UtcNow + delay;
                if (until > state.Until)
                    state.Until = until;

                JacBlackLog.Warning(JacBlackLogCategories.Host,
                    $"{host} просит сбавить обороты (429), ждём {delay.TotalSeconds:F0} с, подряд {state.Strikes}");
            }
        }

        /// <summary>Удачный ответ — счётчик отказов подряд сбрасывается.</summary>
        public static void Ok(string host)
        {
            if (string.IsNullOrWhiteSpace(host) || !_hosts.TryGetValue(host, out var state))
                return;

            if (state.Strikes == 0)
                return;

            lock (state)
                state.Strikes = 0;
        }

        /// <summary>
        /// Retry-After приходит либо числом секунд, либо датой. Обе формы
        /// разрешены, и трекеры пользуются обеими.
        /// </summary>
        static TimeSpan? FromRetryAfter(HttpResponseMessage response)
        {
            var header = response?.Headers?.RetryAfter;
            if (header == null)
                return null;

            if (header.Delta is TimeSpan delta && delta > TimeSpan.Zero)
                return delta;

            if (header.Date is DateTimeOffset date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                    return wait;
            }

            return null;
        }

        /// <summary>Для тестов и диагностики.</summary>
        internal static void Reset(string host)
        {
            if (!string.IsNullOrWhiteSpace(host))
                _hosts.TryRemove(host, out _);
        }
    }
}
