using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Networking;

namespace JacBlack.Infrastructure.Trackers.NNMClub
{
    /// <summary>
    /// Живые сиды с nnmclub — через его собственный поиск.
    ///
    /// Зачем так, а не проще. У закрытых трекеров сиды нельзя узнать ни опросом
    /// анонса (он не отвечает посторонним), ни запуском раздачи в клиенте
    /// (торрент-файлы помечены private, DHT и обмен пирами выключены). Страница
    /// отдельной раздачи тоже не спасает: гостю nnmclub сиды на ней вовсе не
    /// показывает — проверено 31.07.2026.
    ///
    /// Зато его страница поиска отдаёт ЛИСТИНГ, а в листинге колонки сидов и
    /// личей есть, и до полусотни строк приходит за один запрос. То есть на всю
    /// карточку нужен один запрос вместо десятка.
    ///
    /// Тонкость, на которой легко застрять: GET с параметром nm трекер
    /// игнорирует и отдаёт общую свежую выдачу. Работает только POST формой —
    /// замер того же дня: GET вернул 50 строк, из них подходящих 0; POST вернул
    /// 45 строк, все подходящие.
    /// </summary>
    public static class NNMClubSearchSeeders
    {
        /// <summary>Сиды и личи по адресу темы.</summary>
        public sealed class Counts
        {
            public int Sid;
            public int Pir;
        }

        /// <summary>
        /// Спрашивает трекер про одно название и возвращает свежие счётчики,
        /// разложенные по номеру темы. Пустой ответ — не ошибка: раздач может
        /// не быть, и вызывающий просто оставит прежние значения.
        /// </summary>
        /// <summary>
        /// Пропала ли тема. У nnmclub признак честный: пропавшая отдаёт 404,
        /// живая — 200, причём даже гостю (проверено 03.08.2026). Всё
        /// остальное — «не знаю»: 429 и 503 приходят при ограничении частоты,
        /// и принимать их за смерть нельзя.
        /// </summary>
        public static async Task<bool?> IsDeletedAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            try
            {
                // Полное имя: в этом файле есть и System.Net.Http.HttpClient.
                var (_, response) = await Networking.HttpClient.BaseGetAsync(
                    $"{AppInit.conf.NNMClub.host}/forum/viewtopic.php?t={id}",
                    encoding: Encoding.GetEncoding(1251),
                    timeoutSeconds: 12,
                    useproxy: AppInit.conf.NNMClub.useproxy);

                return DeletedReleaseProbe.FromStatusCode(response?.StatusCode ?? System.Net.HttpStatusCode.InternalServerError);
            }
            catch (Exception ex)
            {
                JacBlackLog.Swallowed(JacBlackLogCategories.Trackers, $"nnmclub: тема t={id} не проверена", ex);
                return null;
            }
        }

        public static async Task<Dictionary<string, Counts>> FetchAsync(string title, int timeoutSeconds = 12)
        {
            var result = new Dictionary<string, Counts>(StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(title))
                return result;

            try
            {
                var win1251 = Encoding.GetEncoding(1251);

                // Поля ровно те, что шлёт браузер. f[]=-1 — искать по всем
                // разделам, o=1&s=2 — свежие сверху.
                var form = new List<KeyValuePair<string, string>>
                {
                    new("nm", title),
                    new("f[]", "-1"),
                    new("o", "1"),
                    new("s", "2"),
                    new("submit", "Поиск")
                };

                using var content = new FormUrlEncodedContent(form);
                string html = await Networking.HttpClient.Post(
                    $"{AppInit.conf.NNMClub.rqHost()}/forum/tracker.php",
                    content,
                    encoding: win1251,
                    timeoutSeconds: timeoutSeconds,
                    useproxy: AppInit.conf.NNMClub.useproxy);

                foreach (var row in NNMClubTrackerParser.Parse(html))
                {
                    if (string.IsNullOrEmpty(row.TopicId))
                        continue;

                    // Ноль сидов — законное значение, его тоже отдаём: раздача
                    // могла умереть, и молчать об этом хуже, чем показать ноль.
                    result[row.TopicId] = new Counts { Sid = row.Sid, Pir = row.Pir };
                }
            }
            catch (Exception ex)
            {
                JacBlackLog.Swallowed(JacBlackLogCategories.Trackers, $"nnmclub: живые сиды по «{title}» не получены", ex);
            }

            return result;
        }
    }
}
