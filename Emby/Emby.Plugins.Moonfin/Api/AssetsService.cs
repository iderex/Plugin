using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MediaBrowser.Common;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    public class AssetsService : IService, IRequiresRequest, IHasResultFactory
    {
        private static readonly Lazy<Dictionary<string, string>> ResourceMap = new Lazy<Dictionary<string, string>>(() =>
        {
            var asm = Assembly.GetExecutingAssembly();
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.IndexOf(".assets.", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var idx = name.LastIndexOf(".assets.", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var file = name.Substring(idx + ".assets.".Length);
                if (!dict.ContainsKey(file)) dict[file] = name;
            }
            return dict;
        });

        public IRequest Request { get; set; } = null!;
        public IHttpResultFactory ResultFactory { get; set; } = null!;

        public AssetsService(IApplicationHost appHost)
        {
            ResultFactory = appHost.Resolve<IHttpResultFactory>();
        }

        public object? Get(GetAssetRequest request)
        {
            var fileName = request.FileName?.Trim();
            if (string.IsNullOrEmpty(fileName) || fileName.Length > 128 ||
                fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0 ||
                fileName.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                Request.Response.StatusCode = 404;
                return null;
            }

            if (!ResourceMap.Value.TryGetValue(fileName, out var resName))
            { Request.Response.StatusCode = 404; return null; }

            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName);
            if (stream == null) { Request.Response.StatusCode = 404; return null; }

            Request.Response.AddHeader("Cache-Control", "public,max-age=31536000,immutable");
            Request.Response.AddHeader("X-Content-Type-Options", "nosniff");

            var ct = GetContentType(fileName) ?? "application/octet-stream";
            return ResultFactory.GetResult(Request, stream, ct, null);
        }

        private static string? GetContentType(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".svg" => "image/svg+xml",
                ".webp" => "image/webp",
                _ => null
            };
        }
    }
}
