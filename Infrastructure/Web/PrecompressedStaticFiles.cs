using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace JacBlack.Infrastructure.Web
{
    /// <summary>
    /// Отдаёт файлы фронта, сжатые заранее при сборке, вместо того чтобы жать
    /// их заново на каждый запрос.
    ///
    /// Сборщик кладёт рядом `.br` и `.gz`, но ASP.NET сам их не замечает:
    /// он берёт исходный файл и прогоняет через сжатие на лету. Итог был
    /// хуже вдвойне — 79.9 КБ вместо лежащих на диске 53.2 КБ, и процессор
    /// тратился впустую на каждой загрузке страницы.
    ///
    /// Работает подменой пути: запрос за `app.js` превращается в `app.js.br`,
    /// а заголовки правятся так, чтобы клиент понял, что перед ним сжатое.
    /// </summary>
    public static class PrecompressedStaticFiles
    {
        /// <summary>Что вообще имеет смысл отдавать сжатым.</summary>
        static readonly HashSet<string> Compressible = new(StringComparer.OrdinalIgnoreCase)
        {
            ".js", ".css", ".html", ".json", ".svg", ".xml", ".map", ".yaml", ".yml", ".txt"
        };

        // Порядок важен: brotli сжимает плотнее, поэтому пробуем его первым.
        static readonly (string encoding, string extension)[] Variants =
        {
            ("br", ".br"),
            ("gzip", ".gz")
        };

        const string OriginalPathKey = "precompressed:originalPath";

        public static IApplicationBuilder UsePrecompressedStaticFiles(this IApplicationBuilder app, IWebHostEnvironment env)
        {
            return app.Use(async (context, next) =>
            {
                TryRewrite(context, env);
                await next();
            });
        }

        static void TryRewrite(HttpContext context, IWebHostEnvironment env)
        {
            var path = context.Request.Path.Value;
            if (string.IsNullOrEmpty(path) || context.Request.Method != "GET")
                return;

            int dot = path.LastIndexOf('.');
            if (dot < 0 || !Compressible.Contains(path.Substring(dot)))
                return;

            string accept = context.Request.Headers.AcceptEncoding.ToString();
            if (string.IsNullOrEmpty(accept))
                return;

            foreach (var (encoding, extension) in Variants)
            {
                if (accept.IndexOf(encoding, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var file = env.WebRootFileProvider.GetFileInfo(path + extension);
                if (!file.Exists)
                    continue;

                // Тип содержимого определится по «.br», поэтому исходный путь
                // запоминаем: он понадобится, когда файл уже найден.
                context.Items[OriginalPathKey] = path;
                context.Request.Path = path + extension;

                context.Response.Headers["Content-Encoding"] = encoding;
                context.Response.Headers["Vary"] = "Accept-Encoding";
                return;
            }
        }

        /// <summary>
        /// Возвращает правильный тип содержимого подменённому файлу: клиент
        /// ждёт `application/javascript`, а не тип архива brotli.
        /// </summary>
        public static void FixContentType(HttpContext context, Microsoft.AspNetCore.StaticFiles.IContentTypeProvider contentTypes)
        {
            if (!context.Items.TryGetValue(OriginalPathKey, out var original) || original is not string originalPath)
                return;

            if (contentTypes.TryGetContentType(originalPath, out string contentType))
                context.Response.ContentType = contentType;
        }
    }
}
