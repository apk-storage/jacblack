using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace JacBlack.Infrastructure.Logging
{
    public static class LoggingServiceCollectionExtensions
    {
        public static IServiceCollection AddJacBlackLogging(this IServiceCollection services)
        {
            services.AddSingleton<IConfigureOptions<ConsoleLoggerOptions>, JacBlackConsoleFormatterConfigureOptions>();
            services.AddSingleton<ConsoleFormatter, JacBlackConsoleFormatter>();

            services.AddLogging(builder =>
            {
                builder.AddConsole(options =>
                {
                    options.FormatterName = JacBlackConsoleFormatter.FormatterName;
                });
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddFilter("JacBlack", LogLevel.Debug);
            });

            return services;
        }
    }
}
