namespace JacBlack.Infrastructure.Logging
{
    public static class JacBlackLogCategories
    {
        public const string Tracks = "tracks";
        public const string TracksIndex = "tracks index";
        public const string TracksStats = "tracks stats";
        public const string TracksExport = "tracks export";
        public const string Sync = "sync";
        public const string SyncSpidr = "sync_spidr";
        public const string CronHttp = "cron";
        public const string Fdb = "fdb";
        public const string Stats = "stats";
        public const string Trackers = "trackers";
        public const string Config = "config";
        public const string Host = "host";
        public const string Parser = "parser";

        /// <summary>
        /// Время ответа поиска. Отдельно от parser: ту категорию по умолчанию
        /// гасят целиком, и запись «долгий поиск» с 14.09.2026 не появилась ни разу.
        /// </summary>
        public const string Search = "search";
    }
}
