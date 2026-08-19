using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using JacBlack.Infrastructure.Logging;
using Newtonsoft.Json.Linq;

namespace JacBlack.Infrastructure.Metadata
{
    /// <summary>
    /// Другие названия карточки — те, под которыми вещь известна на трекерах.
    ///
    /// Зачем понадобилось. Заслон карточки сверяет название раздачи с тем, что
    /// прислала Лампа: русским и оригинальным. Для аниме этого мало — там в ходу
    /// японская романизация, которой в карточке нет. «Атака титанов» у нас
    /// называется «Attack on Titan», а на nyaa лежит «Shingeki.Kyojin...», и
    /// заслон честно объявлял такие раздачи чужими: в вебе они были, а из Лампы
    /// не находились ни одной.
    ///
    /// Здесь набор названий расширяется данными TMDB: `alternative_titles` плюс
    /// `original_name`. Ошибиться этим набором сложно — он привязан к той же
    /// карточке по коду IMDB, а не подбирается по похожести строк.
    ///
    /// Правила те же, что у карты сезонов рядом: короткий срок ответа, кеш на
    /// карточку, кеш и на промах — поиск не должен ждать молчащий TMDB дважды.
    /// </summary>
    public static class TmdbAlternativeTitles
    {
        sealed class Entry
        {
            public List<string> Titles;      // null — карточку опознать не удалось
            public DateTime Until;
        }

        static readonly ConcurrentDictionary<string, Entry> _cache =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Названия карточки по коду IMDB: оригинальное, локальные и все
        /// альтернативные. Пустой список — не нашли; вызывающий обязан работать
        /// и без них.
        /// </summary>
        public static List<string> ByImdb(string imdb)
        {
            if (string.IsNullOrWhiteSpace(imdb))
                return null;

            if (_cache.TryGetValue(imdb, out var hit) && hit.Until > DateTime.UtcNow)
                return hit.Titles;

            var conf = AppInit.conf?.tmdb;
            if (conf == null || string.IsNullOrWhiteSpace(conf.apiKey))
                return null;

            List<string> titles = null;
            try
            {
                var (kind, id) = Find(conf, imdb);
                if (id > 0)
                    titles = Titles(conf, kind, id);
            }
            catch (Exception ex)
            {
                JacBlackLog.Warning(JacBlackLogCategories.Trackers,
                    $"tmdb: другие названия по коду {imdb} не получены — {ex.Message}");
            }

            _cache[imdb] = new Entry
            {
                Titles = titles,
                Until = DateTime.UtcNow + (titles != null
                    ? TimeSpan.FromHours(Math.Max(1, conf.cacheHours))
                    : MissTtl)
            };

            return titles;
        }

        static int Seconds(Models.AppConf.TmdbSettings conf) =>
            Math.Max(1, conf.timeoutMs / 1000);

        /// <summary>Что это — фильм или сериал — и его номер в TMDB.</summary>
        static (string kind, int id) Find(Models.AppConf.TmdbSettings conf, string imdb)
        {
            string url = $"https://api.themoviedb.org/3/find/{Uri.EscapeDataString(imdb)}"
                + $"?external_source=imdb_id&api_key={Uri.EscapeDataString(conf.apiKey)}";

            string body = Networking.HttpClient.Get(url, timeoutSeconds: Seconds(conf))
                .GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(body))
                return (null, 0);

            var корень = JObject.Parse(body);
            var tv = корень["tv_results"] as JArray;
            if (tv != null && tv.Count > 0)
                return ("tv", tv[0]?["id"]?.Value<int>() ?? 0);

            var movie = корень["movie_results"] as JArray;
            if (movie != null && movie.Count > 0)
                return ("movie", movie[0]?["id"]?.Value<int>() ?? 0);

            return (null, 0);
        }

        static List<string> Titles(Models.AppConf.TmdbSettings conf, string kind, int id)
        {
            string url = $"https://api.themoviedb.org/3/{kind}/{id}"
                + $"?api_key={Uri.EscapeDataString(conf.apiKey)}"
                + "&append_to_response=alternative_titles";

            string body = Networking.HttpClient.Get(url, timeoutSeconds: Seconds(conf))
                .GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(body))
                return null;

            var корень = JObject.Parse(body);
            var набор = new List<string>();

            void Добавить(string з)
            {
                if (string.IsNullOrWhiteSpace(з)) return;
                з = з.Trim();
                if (з.Length < 2) return;                   // односимвольные ловят всё подряд
                if (!набор.Exists(x => string.Equals(x, з, StringComparison.OrdinalIgnoreCase)))
                    набор.Add(з);
            }

            Добавить(корень["original_name"]?.Value<string>());
            Добавить(корень["original_title"]?.Value<string>());
            Добавить(корень["name"]?.Value<string>());
            Добавить(корень["title"]?.Value<string>());

            // Ключ отличается у сериалов и фильмов: "results" против "titles".
            var альт = корень["alternative_titles"];
            var список = (альт?["results"] as JArray) ?? (альт?["titles"] as JArray);
            if (список != null)
            {
                foreach (var т in список)
                    Добавить(т?["title"]?.Value<string>());
            }

            return набор.Count > 0 ? набор : null;
        }
    }
}
