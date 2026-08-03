using System.Collections.Generic;
using System.Linq;

namespace JacBlack.Infrastructure.Security
{
    /// <summary>Known HTTP routes and expected middleware policy (source of truth for traceability).</summary>
    public static class JacBlackAccessCatalog
    {
        public sealed record RouteEntry(string Path, JacBlackAccessPolicy Policy, string Controller, string Notes = null);

        public static IReadOnlyList<RouteEntry> Routes { get; } = new RouteEntry[]
        {
            // Public — Vue SPA shells & health
            new("/", JacBlackAccessPolicy.Public, "HomeController", "SPA index.html"),
            new("/stats", JacBlackAccessPolicy.Public, "HomeController", "SPA route → index.html"),
            new("/settings", JacBlackAccessPolicy.Public, "HomeController", "SPA route → index.html"),
            new("/opensearch.xml", JacBlackAccessPolicy.Public, "HomeController"),
            new("/health", JacBlackAccessPolicy.Public, "HealthController"),
            new("/version", JacBlackAccessPolicy.Public, "HealthController", "версия протокола для клиентов"),
            new("/version/build", JacBlackAccessPolicy.Public, "HealthController", "сведения о сборке"),
            new("/lastupdatedb", JacBlackAccessPolicy.Public, "HealthController"),
            new("/api/v1.0/conf", JacBlackAccessPolicy.Public, "HealthController", "Jackett apikey probe"),
            new("/openapi.yaml", JacBlackAccessPolicy.Public, "Startup"),
            new("/swagger", JacBlackAccessPolicy.Public, "Swagger"),
            new("/swagger/index.html", JacBlackAccessPolicy.Public, "Swagger"),

            // Public — sync whitelist (opensync checked in SyncController)
            new("/sync/conf", JacBlackAccessPolicy.Public, "SyncController"),
            new("/sync/fdb", JacBlackAccessPolicy.Public, "SyncController", "+ opensync in controller"),
            new("/sync/fdb/torrents", JacBlackAccessPolicy.Public, "SyncController", "+ opensync in controller"),
            new("/sync/torrents", JacBlackAccessPolicy.Public, "SyncController", "returns error"),

            // Config API
            new("/api/v1.0/config", JacBlackAccessPolicy.ConfigApi, "ConfigController"),
            new("/api/v1.0/config/schema", JacBlackAccessPolicy.ConfigApi, "ConfigController"),

            // Dev admin
            new("/dev/updateSize", JacBlackAccessPolicy.DevAdmin, "DevMaintenanceController"),
            new("/dev/FindCorrupt", JacBlackAccessPolicy.DevAdmin, "DevDiagnosticsController"),
            new("/dev/TracksStats", JacBlackAccessPolicy.DevAdmin, "DevTracksController"),
            new("/dev/FixKnabenNames", JacBlackAccessPolicy.DevAdmin, "DevMigrationController"),
            new("/jsondb/save", JacBlackAccessPolicy.DevAdmin, "DbController"),
            new("/cron/rutor/sync", JacBlackAccessPolicy.DevAdmin, "Cron/RutorController"),

            // Search — apikey when configured
            new("/api/v1.0/torrents", JacBlackAccessPolicy.ApiKeyWhenConfigured, "TorrentsController"),
            new("/api/v1.0/qualitys", JacBlackAccessPolicy.ApiKeyWhenConfigured, "TorrentsController"),
            new("/api/v2.0/indexers/all/results", JacBlackAccessPolicy.ApiKeyWhenConfigured, "JackettController"),
            new("/torznab/api", JacBlackAccessPolicy.ApiKeyWhenConfigured, "TorznabController"),
            new("/api/v2.0/indexers", JacBlackAccessPolicy.ApiKeyWhenConfigured, "TorznabController"),
            new("/api/v1/indexer", JacBlackAccessPolicy.ApiKeyWhenConfigured, "TorznabController"),
            new("/api/v1/search", JacBlackAccessPolicy.ApiKeyWhenConfigured, "TorznabController", "Prowlarr Search Feed"),

            // Stats JSON — apikey + openstats in controller (web /stats UI)
            new("/stats/torrents", JacBlackAccessPolicy.ApiKeyWhenConfigured, "StatsController", "+ openstats; stats.json"),
            new("/stats/tracks", JacBlackAccessPolicy.ApiKeyWhenConfigured, "StatsController", "+ openstats; tracks-stats.json"),
            new("/stats/meta", JacBlackAccessPolicy.ApiKeyWhenConfigured, "StatsController", "+ openstats; timestamps"),
        };

        /// <summary>Returns registry mismatches (empty = OK).</summary>
        public static IReadOnlyList<string> VerifyRegistry()
        {
            return Routes
                .Select(r => (r, actual: JacBlackEndpointRegistry.ResolvePolicy(r.Path)))
                .Where(x => x.actual != x.r.Policy)
                .Select(x => $"{x.r.Path}: expected {x.r.Policy}, registry {x.actual} ({x.r.Controller})")
                .ToList();
        }
    }
}
