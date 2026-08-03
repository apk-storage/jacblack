using Microsoft.Extensions.DependencyInjection;

namespace JacBlack.Infrastructure.Security
{
    public static class SecurityServiceCollectionExtensions
    {
        public static IServiceCollection AddJacBlackSecurity(this IServiceCollection services)
        {
            services.AddSingleton<IJacBlackApiKeyValidator, JacBlackApiKeyValidator>();
            services.AddSingleton<IJacBlackDevKeyValidator, JacBlackDevKeyValidator>();
            services.AddSingleton<IJacBlackAccessEvaluator, JacBlackAccessEvaluator>();
            return services;
        }
    }
}
