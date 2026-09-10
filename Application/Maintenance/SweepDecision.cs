using System;
using JacBlack.Models.Details;

namespace JacBlack.Application.Maintenance
{
    /// <summary>
    /// Что делать с раздачей по ответу анонса. Вынесено из записи в базу
    /// отдельно, чтобы правило можно было проверить: раньше оно жило внутри
    /// цикла по шардам и проверялось только на проде, прогоном по миллионам
    /// записей.
    /// </summary>
    public static class SweepDecision
    {
        /// <summary>Чем кончилась проверка одной раздачи.</summary>
        public enum Outcome
        {
            /// <summary>Ни один трекер не ответил — «не знаю», запись не трогаем.</summary>
            Unknown,

            /// <summary>Раздающие есть.</summary>
            Alive,

            /// <summary>Анонс ответил, раздающих ноль.</summary>
            Zero
        }

        public readonly struct Result
        {
            public Outcome Outcome { get; init; }

            /// <summary>Мёртвая ожила: счётчик нулей сброшен.</summary>
            public bool Revived { get; init; }

            /// <summary>Старый снимок сидов заменён подтверждённым нулём.</summary>
            public bool Zeroed { get; init; }

            /// <summary>Нулей подряд набралось столько, что раздача считается мёртвой и прячется из выдачи.</summary>
            public bool ReachedThreshold { get; init; }

            /// <summary>
            /// Нулей набралось столько, что запись можно удалять совсем.
            /// Порог отдельный и много выше: ноль от публичного анонса не
            /// доказывает смерть раздачи, живущей на анонсе своего трекера.
            /// </summary>
            public bool ReachedDeleteThreshold { get; init; }
        }

        /// <summary>
        /// Сколько нулей подряд нужно, чтобы записать ноль в саму раздачу.
        ///
        /// Не с первого: разовый ноль приходит и от сбоя анонса, а обнулить
        /// живую раздачу значит утопить её в сортировке. Со второго — потому
        /// что два независимых прогона подряд ошибаются несравнимо реже, а
        /// ждать дольше незачем: цифра всё это время врёт человеку.
        /// </summary>
        public const int ZeroAfter = 2;

        /// <summary>
        /// Применяет ответ анонса к записи. <paramref name="seeders"/> и
        /// <paramref name="leechers"/> берутся из ответа; <c>null</c> означает,
        /// что не ответил никто.
        /// </summary>
        public static Result Apply(TorrentDetails t, int? seeders, int? leechers, int deadThreshold, int deleteThreshold = int.MaxValue)
        {
            if (t == null || seeders == null)
                return new Result { Outcome = Outcome.Unknown };

            t.lastAliveCheck = DateTime.UtcNow;

            if (seeders > 0)
            {
                bool revived = t.deadChecks > 0;
                t.deadChecks = 0;
                t.sid = seeders.Value;
                t.pir = leechers ?? 0;

                return new Result { Outcome = Outcome.Alive, Revived = revived };
            }

            t.deadChecks++;

            // Подтверждённый ноль записываем в саму раздачу.
            //
            // Раньше круг живости считал нули, но число сидов не трогал — и в
            // базе навсегда оставался снимок вроде «89 раздающих» у раздачи,
            // которой давно нет. Это и была главная болезнь: у rutracker 96%
            // записей не обновлялись больше года, почти все с ненулевыми
            // сидами. Хуже того, заслон мёртвых на выдаче проверяет
            // «deadChecks >= порога И сиды <= 0» — старое число делало его
            // недостижимым, и раздача с тридцатью нулями подряд продолжала
            // показываться бодрой.
            bool zeroed = false;
            if (t.deadChecks >= ZeroAfter && (t.sid > 0 || t.pir > 0))
            {
                t.sid = 0;
                t.pir = 0;
                zeroed = true;
            }

            return new Result
            {
                Outcome = Outcome.Zero,
                Zeroed = zeroed,
                ReachedThreshold = t.deadChecks >= Math.Max(1, deadThreshold),
                ReachedDeleteThreshold = t.deadChecks >= Math.Max(deadThreshold, deleteThreshold)
            };
        }
    }
}
