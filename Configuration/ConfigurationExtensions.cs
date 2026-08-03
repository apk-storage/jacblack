using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JacBlack.Configuration
{
    public static class ConfigurationExtensions
    {
        public static IServiceCollection AddJacBlackConfiguration(this IServiceCollection services)
        {
            AppConfigurationProvider.EnsureInitialized();
            var provider = AppConfigurationProvider.Instance;
            services.AddSingleton(provider);
            services.AddSingleton<IOptionsMonitor<AppOptions>>(provider);
            services.AddHostedService<AppConfigurationReloadWorker>();
            return services;
        }
    }
}
