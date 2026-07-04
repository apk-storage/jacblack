namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Torznab XML (Sonarr/Radarr/Prowlarr). Combined-search tuning — <see cref="SearchSettings"/>.
    /// </summary>
    public class TorznabSettings
    {
        /// <summary>Enable native Torznab XML at /torznab/api and combined search pipeline.</summary>
        public bool enable { get; set; } = true;

        /// <summary>Add voice tags to Torznab titles.</summary>
        public bool enrichTitles { get; set; } = true;

        /// <summary>Skip client-side Torznab category post-filter.</summary>
        public bool skipCatFilter { get; set; } = true;
    }
}
