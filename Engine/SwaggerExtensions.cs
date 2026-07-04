using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using System.Reflection;

namespace JacRed.Engine
{
    public static class SwaggerExtensions
    {
        public static IServiceCollection AddJacRedSwagger(this IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "JacRed API",
                    Version = "v1",
                    Description =
                        "Torrent aggregator API: Jackett-compatible search, native torrent search, stats, sync, config. " +
                        "Interactive UI at <code>/swagger</code>. " +
                        "Static OpenAPI YAML at <a href=\"/openapi.yaml\">/openapi.yaml</a> " +
                        "(<code>wwwroot/openapi.yaml</code>, served via static files)."
                });

                options.DocInclusionPredicate((_, api) =>
                {
                    var path = api.RelativePath ?? "";
                    if (path.StartsWith("cron/", StringComparison.OrdinalIgnoreCase)) return false;
                    if (path.StartsWith("dev/", StringComparison.OrdinalIgnoreCase)) return false;
                    if (path.StartsWith("jsondb/", StringComparison.OrdinalIgnoreCase)) return false;
                    return true;
                });

                options.TagActionsBy(api =>
                {
                    var path = api.RelativePath ?? "";
                    if (path.StartsWith("api/v1.0/config", StringComparison.OrdinalIgnoreCase))
                        return new[] { "Config" };
                    if (path.StartsWith("stats", StringComparison.OrdinalIgnoreCase))
                        return new[] { "Stats" };
                    if (path.StartsWith("sync/", StringComparison.OrdinalIgnoreCase))
                        return new[] { "Sync" };
                    if (path.Contains("torznab", StringComparison.OrdinalIgnoreCase) ||
                        path.Equals("api", StringComparison.OrdinalIgnoreCase))
                        return new[] { "Torznab" };
                    if (path.Contains("indexers", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("torrents", StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("qualitys", StringComparison.OrdinalIgnoreCase))
                        return new[] { "Search" };
                    if (path is "health" or "version" or "lastupdatedb" or "openapi.yaml" ||
                        path.StartsWith("api/v1.0/conf", StringComparison.OrdinalIgnoreCase))
                        return new[] { "System" };
                    return new[] { "Web" };
                });

                options.AddSecurityDefinition("ApiKeyQuery", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Query,
                    Name = "apikey",
                    Description = "API key in query string"
                });

                options.AddSecurityDefinition("ApiKeyHeader", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key",
                    Description = "API key in X-Api-Key header"
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "API key",
                    Description = "API key as Bearer token"
                });

                options.AddSecurityDefinition("DevKeyHeader", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Dev-Key",
                    Description = "Dev key for /api/v1.0/config (when devkey is set in init.yaml)"
                });

                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKeyQuery" }
                        },
                        Array.Empty<string>()
                    },
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKeyHeader" }
                        },
                        Array.Empty<string>()
                    },
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                        },
                        Array.Empty<string>()
                    }
                });

                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                    options.IncludeXmlComments(xmlPath);
            });

            return services;
        }
    }
}
