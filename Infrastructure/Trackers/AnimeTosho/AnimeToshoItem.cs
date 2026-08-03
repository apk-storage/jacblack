using Newtonsoft.Json;

namespace JacBlack.Infrastructure.Trackers.AnimeTosho
{
    /// <summary>
    /// Запись из ленты feed.animetosho.org/json. Отдаётся плоским массивом,
    /// обёртки вокруг результата нет — в отличие от Bitru.
    /// </summary>
    public class AnimeToshoItem
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("torrent_name")]
        public string TorrentName { get; set; }

        [JsonProperty("link")]
        public string Link { get; set; }

        [JsonProperty("magnet_uri")]
        public string MagnetUri { get; set; }

        [JsonProperty("info_hash")]
        public string InfoHash { get; set; }

        [JsonProperty("seeders")]
        public int? Seeders { get; set; }

        [JsonProperty("leechers")]
        public int? Leechers { get; set; }

        [JsonProperty("total_size")]
        public long TotalSize { get; set; }

        [JsonProperty("num_files")]
        public int NumFiles { get; set; }

        /// <summary>Unix-время публикации раздачи.</summary>
        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        /// <summary>complete / skipped и т.п. Берём только complete.</summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>Идентификатор тайтла в AniDB — надёжнее строкового сопоставления.</summary>
        [JsonProperty("anidb_aid")]
        public long? AnidbAid { get; set; }

        [JsonProperty("anidb_eid")]
        public long? AnidbEid { get; set; }

        [JsonProperty("nyaa_id")]
        public long? NyaaId { get; set; }
    }
}
