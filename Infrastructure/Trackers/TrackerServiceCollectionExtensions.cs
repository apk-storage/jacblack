using JacBlack.Infrastructure.Trackers.Anidub;
using JacBlack.Infrastructure.Trackers.Aniliberty;
using JacBlack.Infrastructure.Trackers.AnimeLayer;
using JacBlack.Infrastructure.Trackers.AnimeTosho;
using JacBlack.Infrastructure.Trackers.Bitru;
using JacBlack.Infrastructure.Trackers.Kinozal;
using JacBlack.Infrastructure.Trackers.Knaben;
using JacBlack.Infrastructure.Trackers.Lostfilm;
using JacBlack.Infrastructure.Trackers.Megapeer;
using JacBlack.Infrastructure.Trackers.NNMClub;
using JacBlack.Infrastructure.Trackers.PirateBay;
using JacBlack.Infrastructure.Trackers.Nyaa;
using JacBlack.Infrastructure.Trackers.Rutor;
using JacBlack.Infrastructure.Trackers.Rutracker;
using JacBlack.Infrastructure.Trackers.Selezen;
using JacBlack.Infrastructure.Trackers.Toloka;
using JacBlack.Infrastructure.Trackers.TorrentBy;
using Microsoft.Extensions.DependencyInjection;

namespace JacBlack.Infrastructure.Trackers
{
    public static class TrackerServiceCollectionExtensions
    {
        public static IServiceCollection AddJacBlackTrackers(this IServiceCollection services)
        {
            services.AddSingleton<KnabenSyncService>();
            services.AddSingleton<AnimeLayerSyncService>();
            services.AddSingleton<AnimeToshoSyncService>();
            services.AddSingleton<NyaaSyncService>();
            services.AddSingleton<PirateBaySyncService>();
            services.AddSingleton<Eztv.EztvSyncService>();
            services.AddSingleton<Yts.YtsSyncService>();
            services.AddSingleton<AnilibertySyncService>();
            services.AddSingleton<LostfilmSyncService>();
            services.AddSingleton<RutrackerSyncService>();
            services.AddSingleton<BitruApiSyncService>();
            services.AddSingleton<TorrentBySyncService>();
            services.AddSingleton<MegapeerSyncService>();
            services.AddSingleton<AnidubSyncService>();
            services.AddSingleton<SelezenSyncService>();
            services.AddSingleton<RutorSyncService>();
            services.AddSingleton<NNMClubSyncService>();
            services.AddSingleton<KinozalSyncService>();
            services.AddSingleton<TolokaSyncService>();
            return services;
        }
    }
}
