using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Persistence;

namespace JacRed.Infrastructure.Trackers
{
    /// <summary>
    /// Опознание раздач, которых на трекере больше нет.
    ///
    /// Зачем. Поиск трекера не возвращает удалённую раздачу, но её отсутствие
    /// в выдаче поиска ничего не доказывает: строка могла не попасть в первые
    /// пять страниц, название могло не совпасть, вход мог не пройти. Поэтому
    /// по остатку идём на саму страницу раздачи и слушаем, что трекер скажет.
    ///
    /// Признаки разведаны 03.08.2026 на живых трекерах:
    ///   kinozal — код 200 всегда, но у пропавшей в теле «Нет раздачи с таким
    ///             ID»; страница втрое короче, однако по длине судить НЕЛЬЗЯ:
    ///             без входа и живая, и мёртвая отдают одну и ту же форму
    ///             входа на 4582 байта, и протухшая сессия похоронила бы весь
    ///             трекер разом;
    ///   nnmclub — честный 404, живая тема отдаётся даже гостю;
    ///   toloka  — честный 404, вход отвечает кодом 200, так что спутать не с чем.
    ///
    /// Правило общее с rutracker: помечаем мёртвой, только когда трекер САМ
    /// это сказал. Любая неопределённость — молчим и оставляем как есть.
    /// </summary>
    public static class DeletedReleaseProbe
    {
        /// <summary>
        /// Сколько раздач проверяем за один поиск. Проверка идёт в фоне, но
        /// это всё равно поход на трекер под нашей же учёткой — частить нельзя.
        /// </summary>
        const int MaxPerRun = 3;

        /// <summary>Повторно не спрашиваем про одну и ту же раздачу.</summary>
        static readonly ConcurrentDictionary<string, DateTime> _asked =
            new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        static readonly TimeSpan AskAgainAfter = TimeSpan.FromHours(6);

        /// <summary>
        /// Проверить в фоне. Возвращает сразу: выдача уже собрана и ждать
        /// нечего, а польза придёт следующему запросу — удалённая раздача
        /// отсеется фильтром ещё до сборки ответа.
        /// </summary>
        public static void QueueInBackground(
            string tracker,
            IReadOnlyList<string> ids,
            Func<string, Task<bool?>> isDeleted)
        {
            if (string.IsNullOrWhiteSpace(tracker) || ids == null || ids.Count == 0 || isDeleted == null)
                return;

            var todo = new List<string>();

            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id) || DeadReleases.IsDead(tracker, id))
                    continue;

                string key = tracker + ":" + id;

                if (_asked.TryGetValue(key, out var when) && DateTime.UtcNow - when < AskAgainAfter)
                    continue;

                _asked[key] = DateTime.UtcNow;
                todo.Add(id);

                if (todo.Count >= MaxPerRun)
                    break;
            }

            if (todo.Count == 0)
                return;

            _ = Task.Run(async () =>
            {
                foreach (string id in todo)
                {
                    try
                    {
                        bool? dead = await isDeleted(id);

                        // null — трекер не ответил или мы не вошли. Это не
                        // «раздача жива» и не «раздача мертва», это «не знаем»,
                        // и трогать запись нельзя.
                        if (dead != true)
                            continue;

                        DeadReleases.Remember(tracker, id);

                        JacRedLog.Information(JacRedLogCategories.Trackers,
                            $"{tracker}: раздача {id} удалена с трекера, убрана из выдачи");
                    }
                    catch (Exception ex)
                    {
                        JacRedLog.Swallowed(JacRedLogCategories.Trackers,
                            $"{tracker}: раздача {id} не проверена", ex);
                    }
                }
            });
        }

        /// <summary>
        /// Ответ трекера, у которого пропавшая раздача даёт честный 404.
        /// Всё, что не 404 и не 200, — «не знаем»: 429 и 503 приходят при
        /// ограничении частоты, и принимать их за смерть нельзя.
        /// </summary>
        public static bool? FromStatusCode(System.Net.HttpStatusCode code)
        {
            if (code == System.Net.HttpStatusCode.NotFound)
                return true;

            if (code == System.Net.HttpStatusCode.OK)
                return false;

            return null;
        }
    }
}
