using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Common;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    /// <summary>
    /// Per-user custom ordering of a collection's items. The Moonfin client persists a
    /// drag-and-drop order (CollectionSortOption.custom) that only affects the ordering that one
    /// user sees. GET returns a bare JSON array (empty when unset). POST accepts a bare JSON array body.
    /// </summary>
    public class CollectionsService : IService, IRequiresRequest, IHasResultFactory
    {
        private readonly IAuthorizationContext _authContext;

        public IRequest Request { get; set; } = null!;
        public IHttpResultFactory ResultFactory { get; set; } = null!;

        public CollectionsService(IApplicationHost appHost)
        {
            _authContext = appHost.Resolve<IAuthorizationContext>();
            ResultFactory = appHost.Resolve<IHttpResultFactory>();
        }

        private object Json(object? body) => MoonfinJson.Result(Request, ResultFactory, body);
        private object Json(int statusCode, object? body) { Request.Response.StatusCode = statusCode; return Json(body); }

        public object Get(GetCollectionOrderRequest request)
        {
            var userId = AuthHelpers.GetCurrentUserId(Request, _authContext);
            if (userId == null) return Json(401, new { error = "User not authenticated" });

            var order = CollectionOrderHelper.Get(userId.Value, request.CollectionId);
            return Json(order);
        }

        public async Task<object> Post(SaveCollectionOrderRequest request)
        {
            var userId = AuthHelpers.GetCurrentUserId(Request, _authContext);
            if (userId == null) return Json(401, new { error = "User not authenticated" });

            var itemIds = await MoonfinJson.ReadBodyAsync<List<string>>(request.RequestStream).ConfigureAwait(false)
                ?? new List<string>();

            CollectionOrderHelper.Save(userId.Value, request.CollectionId, itemIds);
            Request.Response.StatusCode = 204;
            return null!;
        }
    }

    /// <summary>
    /// Stores each user's collection order as a JSON array of item-id strings under
    /// <c>collections/{userId}/{collectionId}.json</c>. Item ids are kept opaque (they may be
    /// numeric or GUID). Mirrors <see cref="GameSavesHelper"/>.
    /// </summary>
    internal static class CollectionOrderHelper
    {
        private static readonly object Gate = new object();
        private const int MaxItems = 5000;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = false };

        private static string Root()
        {
            var dataFolder = Plugin.Instance?.DataFolderPath ?? Path.GetTempPath();
            return Path.Combine(dataFolder, "collections");
        }

        public static List<string> Get(Guid userId, string collectionId)
        {
            var path = ResolvePath(userId, collectionId);
            if (path == null || !File.Exists(path)) return new List<string>();
            try
            {
                lock (Gate)
                {
                    var bytes = File.ReadAllBytes(path);
                    return JsonSerializer.Deserialize<List<string>>(bytes, JsonOptions) ?? new List<string>();
                }
            }
            catch
            {
                return new List<string>();
            }
        }

        public static void Save(Guid userId, string collectionId, List<string> itemIds)
        {
            var path = ResolvePath(userId, collectionId);
            if (path == null) throw new ArgumentException("Invalid collection id.", nameof(collectionId));

            var normalized = Normalize(itemIds);

            lock (Gate)
            {
                if (normalized.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(normalized, JsonOptions));
            }
        }

        // Trims blanks, drops duplicates (keeping the first occurrence to preserve drag order), and caps the count.
        private static List<string> Normalize(List<string>? itemIds)
        {
            var result = new List<string>();
            if (itemIds == null) return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in itemIds)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                var trimmed = id.Trim();
                if (!seen.Add(trimmed)) continue;
                result.Add(trimmed);
                if (result.Count >= MaxItems) break;
            }
            return result;
        }

        private static string? ResolvePath(Guid userId, string collectionId)
        {
            var safeCollection = SanitizeFileName(collectionId);
            if (safeCollection.Length == 0) return null;

            var root = Root();
            var path = Path.GetFullPath(Path.Combine(root, userId.ToString("N"), $"{safeCollection}.json"));
            var rootFull = Path.GetFullPath(root);
            var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString()) ? rootFull : rootFull + Path.DirectorySeparatorChar;
            return path.StartsWith(rootWithSep, StringComparison.Ordinal) ? path : null;
        }

        // Collection ids may be numeric or GUID. Keep alphanumerics only so the
        // id can never escape the tree while both forms survive intact.
        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = value.Where(char.IsLetterOrDigit).ToArray();
            var s = new string(chars);
            return s.Length > 128 ? s.Substring(0, 128) : s;
        }
    }
}
