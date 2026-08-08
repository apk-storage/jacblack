using Microsoft.AspNetCore.Http;
using System;

namespace JacBlack.Infrastructure.Security
{
    public static class SecurityHeaders
    {
        public static void Apply(HttpContext httpContext)
        {
            var headers = httpContext.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-Frame-Options"] = "SAMEORIGIN";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            var path = httpContext.Request.Path.Value ?? "";
            if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/openapi.yaml", StringComparison.OrdinalIgnoreCase))
                return;

            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                "base-uri 'self'; " +
                "form-action 'self'; " +
                "frame-ancestors 'self'; " +
                "object-src 'none'; " +
                "script-src 'self'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "font-src 'self'; " +
                "img-src 'self' data:; " +
                // wss: и ws: нужны явно. WebSocket проверяется по connect-src,
                // а схема https: покрывает wss: не во всех браузерах — Chrome
                // по CSP3 пропускает, другие требуют схему буквально. Из-за
                // этого функция «В Лампе» не могла подключиться к сокету CUB
                // (wss://cub.rip:8443): соединение резалось ещё до отправки,
                // и в диалоге это выглядело как «CUB не отвечает».
                "connect-src 'self' https: http: wss: ws:; " +
                "manifest-src 'self'; " +
                "worker-src 'self'";
        }
    }
}
