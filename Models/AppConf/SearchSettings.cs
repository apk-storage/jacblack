namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Combined indexer search (/api/v2.0/indexers/.../results, Torznab when enabled).
    /// </summary>
    public class SearchSettings
    {
        /// <summary>v1 fuzzy merge: false | auto (fuzzy only) | true (always).</summary>
        public string mergeV1 { get; set; } = "auto";

        /// <summary>Max v1 search pairs when mergeV1=auto in fuzzy mode.</summary>
        public int maxV1Pairs { get; set; } = 4;

        /// <summary>v1 sort (sid = seeders desc, pir, size). Also used for IMDB/KP exact v1.</summary>
        public string v1Sort { get; set; } = "sid";

        /// <summary>Strip trailing year from fuzzy query and search both variants.</summary>
        public bool stripTrailingYear { get; set; } = true;
    }
}
