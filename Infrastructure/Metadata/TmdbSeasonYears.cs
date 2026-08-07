using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using JacBlack.Infrastructure.Logging;
using Newtonsoft.Json.Linq;

namespace JacBlack.Infrastructure.Metadata
{
    /// <summary>
    /// Карта «номер сезона → год выхода» для карточки сериала, из TMDB.
    ///
    /// Единственная задача — дать год там, где разбор названия его не дал.
    /// Сценовые релизы год не пишут вовсе («The.Boys.S05E08.1080p.WEB.H264»),
    /// а подставить год премьеры сериала нельзя: карточка «The Boys» датирована
    /// 2019-м, пятый сезон вышел в 2026-м. Разница решает: год в запросе —
    /// условие отбора, и с чужим годом раздача выбывает так же, как без года.
    ///
    /// Запрос идёт ВНУТРИ поиска, поэтому здесь всё подчинено одному: не
    /// заставлять человека ждать. Срок ответа короткий, результат кешируется
    /// на карточку (а не на раздачу), промах кешируется тоже — иначе молчащий
    /// TMDB стоил бы полного ожидания на каждом следующем поиске.
    ///
    /// Опознаём карточку по коду IMDB — он к этому моменту уже выведен из
    /// выдачи. Запасной путь по названию и году намеренно НЕ делаем: на тёзках
    /// он ошибается молча, а ошибка здесь дороже пропуска — чужая карта
    /// сезонов подставит неверные годы и выбросит верные раздачи.
    /// </summary>
    public static class TmdbSeasonYears
    {
        sealed class Entry
        {
            public Dictionary<int, int> Years;   // null — карточку опознать не удалось
            public DateTime Until;
        }

        static readonly ConcurrentDictionary<string, Entry> _cache =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Промах держим недолго: TMDB мог просто не ответить.</summary>
        static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Годы сезонов сериала по коду IMDB. Возвращает null, если TMDB
        /// выключен, код неизвестен или карточка не опознана — вызывающий
        /// обязан работать и без карты.
        /// </summary>
        public static Dictionary<int, int> ByImdb(string imdb)
        {
            if (string.IsNullOrWhiteSpace(imdb))
                return null;

            // Кеш смотрим ДО настроек. Уже добытая карта остаётся годной, даже
            // если ключ потом убрали, — а обратный порядок молча выбрасывал
            // готовый ответ и заставлял ходить в сеть заново.
            if (_cache.TryGetValue(imdb, out var hit) && hit.Until > DateTime.UtcNow)
                return hit.Years;

            var conf = AppInit.conf?.tmdb;
            if (conf == null || string.IsNullOrWhiteSpace(conf.apiKey))
                return null;

            Dictionary<int, int> years = null;
            try
            {
                int tv = FindTvId(conf, imdb);
                if (tv > 0)
                    years = SeasonYears(conf, tv);
            }
            catch (Exception ex)
            {
                // Молчать нельзя: беззвучно выключившийся TMDB выглядит как
                // «раздачи опять пропали», и искать причину пришлось бы заново.
                JacBlackLog.Warning(JacBlackLogCategories.Trackers,
                    $"tmdb: годы сезонов по коду {imdb} не получены — {ex.Message}");
            }

            _cache[imdb] = new Entry
            {
                Years = years,
                Until = DateTime.UtcNow + (years != null
                    ? TimeSpan.FromHours(Math.Max(1, conf.cacheHours))
                    : MissTtl)
            };

            return years;
        }

        static int Seconds(Models.AppConf.TmdbSettings conf) =>
            Math.Max(1, conf.timeoutMs / 1000);

        static int FindTvId(Models.AppConf.TmdbSettings conf, string imdb)
        {
            string url = $"https://api.themoviedb.org/3/find/{Uri.EscapeDataString(imdb)}"
                + $"?external_source=imdb_id&api_key={Uri.EscapeDataString(conf.apiKey)}";

            string body = Networking.HttpClient.Get(url, timeoutSeconds: Seconds(conf))
                .GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(body))
                return 0;

            var results = JObject.Parse(body)["tv_results"] as JArray;
            if (results == null || results.Count == 0)
                return 0;

            return results[0]?["id"]?.Value<int>() ?? 0;
        }

        static Dictionary<int, int> SeasonYears(Models.AppConf.TmdbSettings conf, int tvId)
        {
            string url = $"https://api.themoviedb.org/3/tv/{tvId}"
                + $"?api_key={Uri.EscapeDataString(conf.apiKey)}"
                + $"&language={Uri.EscapeDataString(conf.language ?? "ru-RU")}";

            string body = Networking.HttpClient.Get(url, timeoutSeconds: Seconds(conf))
                .GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(body))
                return null;

            var seasons = JObject.Parse(body)["seasons"] as JArray;
            if (seasons == null)
                return null;

            var map = new Dictionary<int, int>();
            foreach (var s in seasons)
            {
                int? number = s?["season_number"]?.Value<int?>();
                string air = s?["air_date"]?.Value<string>();

                // Анонсированный сезон приходит без даты — пропускаем: пустая
                // запись хуже отсутствующей, она подставила бы нулевой год.
                if (number == null || string.IsNullOrWhiteSpace(air))
                    continue;

                if (DateTime.TryParse(air, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt) && dt.Year > 1900)
                    map[number.Value] = dt.Year;
            }

            return map.Count > 0 ? map : null;
        }

        /// <summary>Для тестов: карта задаётся напрямую, сеть не трогается.</summary>
        internal static void Seed(string imdb, Dictionary<int, int> years, TimeSpan ttl)
        {
            if (string.IsNullOrWhiteSpace(imdb))
                return;

            _cache[imdb] = new Entry { Years = years, Until = DateTime.UtcNow + ttl };
        }

        /// <summary>Для тестов: забыть накопленное.</summary>
        internal static void Reset() => _cache.Clear();
    }
}
