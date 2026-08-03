using Newtonsoft.Json;

namespace JacRed.Infrastructure.Trackers.Eztv
{
    /// <summary>
    /// Запись из открытого API EZTV.
    ///
    /// Источник англоязычных сериалов: авторизация не нужна, magnet приходит
    /// готовым — вместе со списком трекеров, так что раздача не остаётся жить
    /// на одном DHT.
    ///
    /// В ответе стоит <see cref="EztvResponse.TorrentsCount"/> под миллион, но
    /// реально доступно около десяти тысяч: листание упирается в потолок на
    /// сотой странице (замерено 30.07.2026). Верить этому счётчику нельзя.
    ///
    /// Русской озвучки там нет. Заводится не ради неё, а ради двух вещей:
    /// свежие серии появляются быстро, и — главное — вместе с раздачей приходит
    /// КОД IMDB. Именно он позволяет собрать словарь «код → название» из своей
    /// базы и отвечать на поиск по коду без чужого сервиса.
    /// </summary>
    public class EztvItem
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("magnet_url")]
        public string MagnetUrl { get; set; }

        /// <summary>Код IMDB БЕЗ префикса tt: «31596422». Префикс дописываем сами.</summary>
        [JsonProperty("imdb_id")]
        public string ImdbId { get; set; }

        [JsonProperty("season")]
        public string Season { get; set; }

        [JsonProperty("episode")]
        public string Episode { get; set; }

        [JsonProperty("seeds")]
        public int Seeds { get; set; }

        [JsonProperty("peers")]
        public int Peers { get; set; }

        /// <summary>Время публикации, unix-секунды.</summary>
        [JsonProperty("date_released_unix")]
        public long DateReleasedUnix { get; set; }

        /// <summary>Размер в байтах, приходит строкой.</summary>
        [JsonProperty("size_bytes")]
        public string SizeBytes { get; set; }
    }

    public class EztvResponse
    {
        [JsonProperty("torrents_count")]
        public long TorrentsCount { get; set; }

        [JsonProperty("torrents")]
        public EztvItem[] Torrents { get; set; }
    }
}
