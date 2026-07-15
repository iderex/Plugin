using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MediaBrowser.Model.Logging;

namespace Emby.Plugins.Moonfin.Services
{
    public class MdbListCacheService : FileBackedCache<MdbListCacheEntry>
    {
        public MdbListCacheService(ILogger logger) : base(logger, "mdblist_cache.json", "MDBList") { }

        public List<MdbListRating>? TryGet(string cacheKey, TimeSpan maxAge)
        {
            var cache = EnsureLoaded();
            if (cache.TryGetValue(cacheKey, out var entry) && DateTimeOffset.UtcNow - entry.CachedAt < maxAge)
                return entry.Ratings;
            return null;
        }

        public void Set(string cacheKey, List<MdbListRating> ratings)
        {
            var cache = EnsureLoaded();
            cache[cacheKey] = new MdbListCacheEntry { Ratings = ratings, CachedAt = DateTimeOffset.UtcNow };
        }

        public void SetMany(Dictionary<string, List<MdbListRating>> items)
        {
            var cache = EnsureLoaded();
            var now = DateTimeOffset.UtcNow;
            foreach (var kvp in items)
                cache[kvp.Key] = new MdbListCacheEntry { Ratings = kvp.Value, CachedAt = now };
        }

        public HashSet<string> GetFreshKeys(TimeSpan maxAge)
        {
            var cache = EnsureLoaded();
            var cutoff = DateTimeOffset.UtcNow - maxAge;
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in cache)
                if (kvp.Value.CachedAt >= cutoff) keys.Add(kvp.Key);
            return keys;
        }
    }

    public class MdbListCacheEntry
    {
        [JsonPropertyName("ratings")] public List<MdbListRating> Ratings { get; set; } = new List<MdbListRating>();
        [JsonPropertyName("cachedAt")] public DateTimeOffset CachedAt { get; set; }
    }

    public class MdbListRating
    {
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("value")] public double? Value { get; set; }
        [JsonPropertyName("score")] public double? Score { get; set; }
        [JsonPropertyName("votes")] public int? Votes { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}
