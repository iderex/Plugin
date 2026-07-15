using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.Moonfin.Api
{
    /// <summary>
    /// JSON serialization that emits member names verbatim with <see cref="JsonPropertyName"/>
    /// overrides honored. Emby's own IService serializer ignores those attributes, so every
    /// endpoint serializes through this instead.
    /// </summary>
    internal static class MoonfinJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        /// <summary>Serializes the body and returns it as an Emby JSON result, preserving any status code already set on the response.</summary>
        public static object Result(IRequest request, IHttpResultFactory resultFactory, object? body)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body ?? new { }, Options);
            return resultFactory.GetResult(request, new MemoryStream(bytes), "application/json", null);
        }

        /// <summary>
        /// Deserializes a request body stream. Returns default on empty or invalid body.
        /// Reads asynchronously: Emby's request stream (Kestrel) forbids synchronous reads.
        /// </summary>
        public static async Task<T?> ReadBodyAsync<T>(Stream? stream)
        {
            if (stream == null) return default;

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(false);
            if (ms.Length == 0) return default;

            try { return JsonSerializer.Deserialize<T>(ms.ToArray(), Options); }
            catch (JsonException) { return default; }
        }
    }
}
