using Microsoft.AspNetCore.Http;

namespace JacBlack.Infrastructure.Security
{
    public interface IJacBlackAccessEvaluator
    {
        JacBlackAccessResult EvaluatePath(string path, HttpContext httpContext);
        bool ShouldSetPrivateNetworkHeader(IClientNetworkContext network, string path);
    }

    public sealed class JacBlackAccessEvaluator : IJacBlackAccessEvaluator
    {
        readonly IJacBlackApiKeyValidator _apiKeyValidator;
        readonly IJacBlackDevKeyValidator _devKeyValidator;

        public JacBlackAccessEvaluator(IJacBlackApiKeyValidator apiKeyValidator, IJacBlackDevKeyValidator devKeyValidator)
        {
            _apiKeyValidator = apiKeyValidator;
            _devKeyValidator = devKeyValidator;
        }

        public JacBlackAccessResult EvaluatePath(string path, HttpContext httpContext)
        {
            var policy = JacBlackEndpointRegistry.ResolvePolicy(path);
            var network = ClientNetworkContext.From(httpContext);
            var method = httpContext.Request.Method;

            if (policy == JacBlackAccessPolicy.DevAdmin)
                return EvaluateDevAdmin(network, httpContext, method);

            if (policy == JacBlackAccessPolicy.ConfigApi)
                return EvaluateConfigApi(network, httpContext, method);

            if (policy == JacBlackAccessPolicy.ApiKeyWhenConfigured && _apiKeyValidator.IsConfigured)
            {
                if (_apiKeyValidator.Validate(httpContext))
                    return JacBlackAccessResult.Allow;

                return JacBlackAccessResult.Deny(
                    DenyStatus(keyConfigured: true, method),
                    setPrivateNetworkHeader: ShouldSetPrivateNetworkHeader(network, path));
            }

            return JacBlackAccessResult.Allow;
        }

        public bool ShouldSetPrivateNetworkHeader(IClientNetworkContext network, string path)
            => network.IsTrustedContext || !JacBlackEndpointRegistry.IsRestrictedAdminPath(path);

        JacBlackAccessResult EvaluateDevAdmin(IClientNetworkContext network, HttpContext httpContext, string method)
        {
            if (IsDevEndpointAccessAllowed(network, httpContext))
                return JacBlackAccessResult.Allow;
            return JacBlackAccessResult.Deny(DenyStatus(_devKeyValidator.IsConfigured, method));
        }

        JacBlackAccessResult EvaluateConfigApi(IClientNetworkContext network, HttpContext httpContext, string method)
            => EvaluateDevAdmin(network, httpContext, method);

        static bool IsDevEndpointAccessAllowed(IClientNetworkContext network, HttpContext httpContext)
        {
            if (IsTrustedLanClient(network, httpContext))
                return true;
            return JacBlackKeyUtils.DevKeyMatches(httpContext, AppInit.conf?.devkey);
        }

        /// <summary>
        /// LAN / direct localhost. Same-host reverse proxy (cloudflared on 127.0.0.1) alone is not enough.
        /// </summary>
        static bool IsTrustedLanClient(IClientNetworkContext network, HttpContext httpContext)
        {
            if (!network.IsDirectLocalClient)
                return false;

            // cloudflared/nginx on loopback: peer is 127.0.0.1; without real client IP the request looks local.
            if (network.IsSameHostReverseProxy && ClientNetworkContext.HasProxyClientIdentityHeaders(httpContext))
                return false;

            return true;
        }

        public static int DenyStatus(bool keyConfigured, string method)
            => method == "OPTIONS" ? 204 : (keyConfigured ? 401 : 403);
    }
}
