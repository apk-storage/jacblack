using Newtonsoft.Json;

namespace JacRed.Infrastructure.Trackers.Yts
{
    /// <summary>
    /// Фильм из открытого API YTS.
    ///
    /// Только фильмы, около 76 тысяч, оригинальная дорожка чаще английская
    /// (примерно три пятых), встречаются французские, японские, испанские,
    /// корейские. Русской озвучки нет.
    ///
    /// Фирменная черта — сильное сжатие: «1080p» у них весит около полутора
    /// гигабайт вместо привычных десяти. На телефоне разница незаметна, на
    /// большом экране видна. В Лампе есть фильтр по трекерам.
    ///
    /// Главная ценность для нас — поле imdb_code: код приходит вместе с
    /// названием и годом, из чего собирается словарь для поиска по коду.
    /// </summary>
    public class YtsMovie
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        /// <summary>Код IMDB уже с префиксом: «tt2835494».</summary>
        [JsonProperty("imdb_code")]
        public string ImdbCode { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("title_english")]
        public string TitleEnglish { get; set; }

        [JsonProperty("title_long")]
        public string TitleLong { get; set; }

        [JsonProperty("year")]
        public int Year { get; set; }

        /// <summary>Язык оригинала двумя буквами: en, fr, ja.</summary>
        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("torrents")]
        public YtsTorrent[] Torrents { get; set; }
    }

    /// <summary>Один вариант качества у фильма. У одного фильма их обычно два-четыре.</summary>
    public class YtsTorrent
    {
        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("quality")]
        public string Quality { get; set; }

        /// <summary>Источник: web, bluray.</summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("video_codec")]
        public string VideoCodec { get; set; }

        [JsonProperty("size_bytes")]
        public long SizeBytes { get; set; }

        [JsonProperty("seeds")]
        public int Seeds { get; set; }

        [JsonProperty("peers")]
        public int Peers { get; set; }

        [JsonProperty("date_uploaded_unix")]
        public long DateUploadedUnix { get; set; }
    }

    public class YtsResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("data")]
        public YtsData Data { get; set; }
    }

    public class YtsData
    {
        [JsonProperty("movie_count")]
        public long MovieCount { get; set; }

        [JsonProperty("movies")]
        public YtsMovie[] Movies { get; set; }
    }
}
