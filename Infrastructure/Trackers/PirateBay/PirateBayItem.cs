using Newtonsoft.Json;

namespace JacBlack.Infrastructure.Trackers.PirateBay
{
    /// <summary>
    /// Запись из открытого API The Pirate Bay (apibay.org).
    ///
    /// Заведено 29.07.2026 как «массовый» источник: у русских трекеров сиды
    /// обычно единицы и десятки, а здесь в HD-разделах **медиана больше
    /// тысячи**, максимум за десять тысяч. Русской озвучки там почти нет,
    /// но в Лампе есть фильтр по трекерам — кому нужен оригинал, тот выберет.
    ///
    /// Авторизация не нужна, хеш приходит прямо в ответе, поэтому magnet
    /// собирается без единого дополнительного запроса.
    /// </summary>
    public class PirateBayItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("info_hash")]
        public string InfoHash { get; set; }

        [JsonProperty("seeders")]
        public string Seeders { get; set; }

        [JsonProperty("leechers")]
        public string Leechers { get; set; }

        [JsonProperty("size")]
        public string Size { get; set; }

        /// <summary>Время добавления, unix-секунды строкой.</summary>
        [JsonProperty("added")]
        public string Added { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        /// <summary>Идентификатор IMDb, если указан загрузившим.</summary>
        [JsonProperty("imdb")]
        public string Imdb { get; set; }
    }
}
