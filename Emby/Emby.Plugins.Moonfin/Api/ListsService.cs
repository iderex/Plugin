using System;
using System.Linq;
using Emby.Plugins.Moonfin.Services;
using MediaBrowser.Common;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    public class ListsService : IService, IRequiresRequest, IHasResultFactory
    {
        // Lenient read TTL so a stale cache still serves rows rather than returning nothing
        // if the sync task has not run for a while.
        private static readonly TimeSpan ReadTtl = TimeSpan.FromDays(30);

        public IRequest Request { get; set; } = null!;
        public IHttpResultFactory ResultFactory { get; set; } = null!;

        private MdbListListsCacheService Cache => Plugin.Instance?.MdbListListsCache
            ?? throw new InvalidOperationException("MdbListListsCacheService not initialized");

        public ListsService(IApplicationHost appHost)
        {
            ResultFactory = appHost.Resolve<IHttpResultFactory>();
        }

        private object Json(object? body) => MoonfinJson.Result(Request, ResultFactory, body);
        private object Json(int statusCode, object? body) { Request.Response.StatusCode = statusCode; return Json(body); }

        public object Get(GetMdbListsRequest request)
        {
            var catalog = Cache.TryGetCatalog(ReadTtl);
            if (catalog == null)
                return Json(new { success = false, error = "No MDBList lists cached yet. Ask your server admin to set a server-wide MDBList key and run the Moonfin MDBList Official Lists Sync task.", lists = Array.Empty<object>() });

            return Json(new { success = true, lists = catalog });
        }

        public object Get(GetMdbListItemsRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Slug))
                return Json(400, new { success = false, error = "Missing required path parameter: slug" });

            string? typeFilter = null;
            if (!string.IsNullOrWhiteSpace(request.Mediatype))
            {
                typeFilter = request.Mediatype!.Trim().ToLowerInvariant();
                if (typeFilter != "movie" && typeFilter != "show")
                    return Json(400, new { success = false, error = "Invalid mediatype. Expected: movie or show" });
            }

            var items = Cache.TryGetItems(request.Slug!.Trim(), ReadTtl);
            if (items == null)
                return Json(new { success = false, slug = request.Slug, error = "This list is not cached yet. It may sync on the next run of the Moonfin MDBList Official Lists Sync task.", items = Array.Empty<object>() });

            if (typeFilter != null)
                items = items.Where(i => string.Equals(i.Type, typeFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            return Json(new { success = true, slug = request.Slug, items });
        }
    }
}
