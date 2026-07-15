using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Emby.Plugins.Moonfin
{
    /// <summary>
    /// Shared HTTP helpers. Emby has no IHttpClientFactory, so clients are hand-built here.
    /// Lives in the root namespace so both the Api and Services types reach it without a using.
    /// </summary>
    internal static class MoonfinHttp
    {
        /// <summary>A per-call HttpClient with the given timeout and User-Agent. The caller disposes it.</summary>
        public static HttpClient CreateClient(TimeSpan timeout, string userAgent)
        {
            var client = new HttpClient { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            return client;
        }

        /// <summary>
        /// Applies a TMDB key to a request. v4 read tokens (eyJ...) use a Bearer header,
        /// v3 keys go in the query string.
        /// </summary>
        public static void ApplyTmdbAuth(HttpRequestMessage request, string apiKey)
        {
            if (apiKey.StartsWith("eyJ", StringComparison.Ordinal))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            else
            {
                var builder = new UriBuilder(request.RequestUri!);
                var sep = builder.Query.Length > 1 ? "&" : "?";
                builder.Query = builder.Query.TrimStart('?') + sep + "api_key=" + Uri.EscapeDataString(apiKey);
                request.RequestUri = builder.Uri;
            }
        }
    }
}
