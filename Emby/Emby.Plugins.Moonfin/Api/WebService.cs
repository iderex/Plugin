using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using MediaBrowser.Common;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    public class WebService : IService, IRequiresRequest, IHasResultFactory
    {
        private readonly Assembly _assembly = typeof(WebService).Assembly;

        public IRequest Request { get; set; } = null!;
        public IHttpResultFactory ResultFactory { get; set; } = null!;

        public WebService(IApplicationHost appHost)
        {
            ResultFactory = appHost.Resolve<IHttpResultFactory>();
        }

        public object Get(GetPluginJsRequest request)
        {
            const string script = "(function(){if(window.location.pathname.toLowerCase().indexOf('/moonfin/web/')===-1){window.location.href='/Moonfin/Web/';}})();";
            return ResultFactory.GetResult(Request, ToStream(script), "application/javascript", null);
        }

        public object Get(GetPluginCssRequest request)
        {
            return ResultFactory.GetResult(Request, ToStream("/* moonfin css stub */"), "text/css", null);
        }

        public object Get(GetLoaderJsRequest request)
        {
            var stream = _assembly.GetManifestResourceStream("Emby.Plugins.Moonfin.Web.loader.js");
            if (stream == null) { Request.Response.StatusCode = 404; return new { error = "loader.js not found" }; }
            return ResultFactory.GetResult(Request, stream, "application/javascript", null);
        }

        public object Get(GetWebConfigRequest request)
        {
            var config = Plugin.Instance?.Configuration;
            var runtimeBaseUrl = ResolveRuntimeBaseUrl();
            var payload = new
            {
                schemaVersion = 1,
                defaultServerUrl = NormalizeConfiguredServerUrl(config?.WebDefaultServerUrl) ?? runtimeBaseUrl,
                discoveryProxyUrl = $"{runtimeBaseUrl}/Moonfin/Discovery/",
                enableWebRtcScan = config?.WebEnableWebRtcScan ?? true,
                brandingName = "Moonfin",
                pluginMode = true,
                forcedServerUrl = NormalizeConfiguredServerUrl(config?.WebForcedServerUrl)
            };

            Request.Response.AddHeader("Cache-Control", "no-store, no-cache, must-revalidate");
            Request.Response.AddHeader("Pragma", "no-cache");

            var json = JsonSerializer.Serialize(payload);
            return ResultFactory.GetResult(Request, ToStream(json), "application/json", null);
        }

        public object Get(GetWebAssetRequest request)
        {
            var webRoot = ResolveWebRoot();
            if (string.IsNullOrWhiteSpace(webRoot) || !Directory.Exists(webRoot))
            {
                Request.Response.StatusCode = 404;
                return new { error = "Moonfin web root not found" };
            }

            var requestedPath = string.IsNullOrWhiteSpace(request.Path) ? "index.html" : request.Path;
            if (!TryResolvePath(webRoot, requestedPath, out var fullPath))
            { Request.Response.StatusCode = 404; return null!; }

            if (File.Exists(fullPath))
            {
                return ResultFactory.GetStaticFileResult(Request, fullPath);
            }

            if (Directory.Exists(fullPath))
            {
                var nested = Path.Combine(fullPath, "index.html");
                if (File.Exists(nested))
                {
                    // The request resolved to a directory's index (e.g. the theme editor at /theme).
                    // If the URL lacks a trailing slash, redirect to add one so the browser resolves
                    // the page's relative asset paths against the directory instead of its parent.
                    try
                    {
                        var uri = new Uri(Request.AbsoluteUri);
                        if (!uri.AbsolutePath.EndsWith("/"))
                        {
                            Request.Response.StatusCode = 302;
                            Request.Response.AddHeader("Location", uri.AbsolutePath + "/" + uri.Query);
                            return null!;
                        }
                    }
                    catch { /* fall through to serving the index directly */ }

                    return ResultFactory.GetStaticFileResult(Request, nested);
                }
            }

            if (Path.HasExtension(requestedPath)) { Request.Response.StatusCode = 404; return null!; }

            var indexPath = Path.Combine(webRoot, "index.html");
            if (!File.Exists(indexPath)) { Request.Response.StatusCode = 404; return null!; }
            return ResultFactory.GetStaticFileResult(Request, indexPath);
        }

        private string ResolveRuntimeBaseUrl()
        {
            var scheme = Request.IsLocal ? "http" : "https";
            var host = Request.Headers["Host"] ?? "localhost";
            var baseUrl = $"{scheme}://{host}".TrimEnd('/');

            // Honor a reverse-proxy sub-path so Moonfin web served under a mount point
            // (e.g. /moonfin) builds correct base, asset, and discovery URLs. Emby's request
            // has no PathBase, so we read the forwarded-prefix header the proxy sets.
            var prefix = Request.Headers["X-Forwarded-Prefix"];
            if (string.IsNullOrEmpty(prefix)) prefix = Request.Headers["X-Forwarded-Path"];
            prefix = NormalizeForwardedPrefix(prefix);
            return prefix.Length == 0 ? baseUrl : baseUrl + prefix;
        }

        private static string NormalizeForwardedPrefix(string? prefix)
        {
            prefix = prefix?.Trim();
            if (string.IsNullOrEmpty(prefix) || prefix == "/") return string.Empty;
            if (!prefix.StartsWith("/")) prefix = "/" + prefix;
            return prefix.TrimEnd('/');
        }

        private static string? NormalizeConfiguredServerUrl(string? value)
        {
            value = value?.Trim();
            if (string.IsNullOrEmpty(value)) return null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value.TrimEnd('/');

            var segments = new List<string>(
                uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
            var count = segments.Count;
            if (count >= 2 && string.Equals(segments[count - 2], "web", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[count - 1], "index.html", StringComparison.OrdinalIgnoreCase))
                segments.RemoveRange(count - 2, 2);
            else if (count >= 1 && string.Equals(segments[count - 1], "web", StringComparison.OrdinalIgnoreCase))
                segments.RemoveAt(count - 1);

            var path = segments.Count == 0 ? string.Empty : "/" + string.Join("/", segments);
            return new UriBuilder(uri) { Path = path, Query = string.Empty, Fragment = string.Empty }.Uri
                .GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        private string ResolveWebRoot()
        {
            var envOverride = Environment.GetEnvironmentVariable("MOONFIN_WEB_ROOT")?.Trim();
            if (!string.IsNullOrEmpty(envOverride) && Directory.Exists(envOverride)) return Path.GetFullPath(envOverride);

            var assemblyDir = Path.GetDirectoryName(_assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                foreach (var folder in new[] { "web", "frontend" })
                {
                    var candidate = Path.Combine(assemblyDir, folder);
                    if (Directory.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            }

            var dataPath = Plugin.Instance?.DataFolderPath;
            if (!string.IsNullOrEmpty(dataPath))
                return Path.GetFullPath(Path.Combine(dataPath, "web"));

            return string.Empty;
        }

        private static bool TryResolvePath(string rootPath, string requestPath, out string fullPath)
        {
            var normalizedRequest = requestPath.Replace('\\', '/').TrimStart('/');
            var candidate = Path.Combine(rootPath, normalizedRequest.Replace('/', Path.DirectorySeparatorChar));
            fullPath = Path.GetFullPath(candidate);

            var normalizedRoot = Path.GetFullPath(rootPath);
            var rootWithSep = normalizedRoot.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? normalizedRoot : normalizedRoot + Path.DirectorySeparatorChar;
            var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return fullPath.StartsWith(rootWithSep, comparison) || string.Equals(fullPath, normalizedRoot, comparison);
        }

        private static Stream ToStream(string text) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
    }
}
