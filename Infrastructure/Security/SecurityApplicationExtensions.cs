using Microsoft.AspNetCore.Builder;

namespace JacBlack.Infrastructure.Security
{
    public static class SecurityApplicationExtensions
    {
        public static IApplicationBuilder UseJacBlackSecurity(this IApplicationBuilder builder)
        {
            return builder
                .UseMiddleware<SecurityHeadersMiddleware>()
                .UseMiddleware<JacBlackAuthorizationMiddleware>();
        }
    }
}
