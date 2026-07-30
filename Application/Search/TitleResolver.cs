using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Networking;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;

namespace JacRed.Application.Search
{
    /// <summary>
    /// Превращает `tt0111161` или `kp326` в название фильма: по идентификатору
    /// в базе искать нечего.
    ///
    /// Раньше этот кусок стоял двумя копиями — в родном API и в API индексаторов,
    /// с разными ключами кеша и чуть разным поведением при отказе. Сведён в одно
    /// место, адрес и токен переехали в настройки.
    /// </summary>
    public static class TitleResolver
    {
        static readonly Regex RxId = new Regex("^(tt|kp)[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static int _warnedNoToken;

        /// <summary>Запрос выглядит как идентификатор, а не как название.</summary>
        public static bool LooksLikeId(string search) =>
            !string.IsNullOrWhiteSpace(search) && RxId.IsMatch(search.Trim());

        /// <summary>
        /// Возвращает пару «оригинальное название, локальное название».
        /// Если запрос не идентификатор, служба выключена или название не нашлось —
        /// отдаёт исходные значения, чтобы вызывающий код не различал случаи.
        /// </summary>
        public static async Task<(string search, string altname)> ResolveAsync(string search, string altname, IMemoryCache cache)
        {
            if (!LooksLikeId(search))
                return (search, altname);

            var conf = AppInit.conf?.titleapi;
            if (conf == null || !conf.enable)
                return (search, altname);

            if (string.IsNullOrWhiteSpace(conf.token))
            {
                // Одного предупреждения достаточно: иначе оно польётся на каждый запрос.
                if (Interlocked.Exchange(ref _warnedNoToken, 1) == 0)
                {
                    JacRedLog.Warning(JacRedLogCategories.Host,
                        "поиск по идентификатору kp/imdb пришёл, но titleapi.token не задан — название узнать не у кого");
                }

                return (search, altname);
            }

            string id = search.Trim();
            string memkey = $"titleapi:{id.ToLowerInvariant()}";

            if (cache == null || !cache.TryGetValue(memkey, out (string original_name, string name) found))
            {
                string query = id.StartsWith("kp", StringComparison.OrdinalIgnoreCase)
                    ? $"&kp={id.Substring(2)}"
                    : $"&imdb={id}";

                string url = $"{conf.url.TrimEnd('/')}/?token={conf.token}{query}";

                var root = await HttpClient.Get<JObject>(url, timeoutSeconds: conf.timeoutSeconds);

                found.original_name = root?.Value<JObject>("data")?.Value<string>("original_name");
                found.name = root?.Value<JObject>("data")?.Value<string>("name");

                if (found.original_name == null && found.name == null)
                {
                    JacRedLog.Warning(JacRedLogCategories.Host,
                        $"titleapi не вернул название для {id}: {root?.Value<string>("error_info") ?? "пустой ответ"}");
                }

                cache?.Set(memkey, found, DateTime.UtcNow.AddHours(conf.cacheHours <= 0 ? 24 : conf.cacheHours));
            }

            if (!string.IsNullOrWhiteSpace(found.name) && !string.IsNullOrWhiteSpace(found.original_name))
                return (found.original_name, found.name);

            // Нашлось только одно из двух — ищем по нему; ничего не нашлось — оставляем как было.
            return (found.original_name ?? found.name ?? search, altname);
        }
    }
}
