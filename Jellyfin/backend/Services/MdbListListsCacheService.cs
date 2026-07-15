using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Moonfin.Server.Api;

namespace Moonfin.Server.Services;

/// <summary>
/// Persistent file-backed cache for MDBList official lists.
/// Holds the list catalog under the key "catalog" and each list's items under "items:{slug}".
/// The lists sync task populates this cache; the controller reads from it.
/// Mirrors <see cref="MdbListCacheService"/> (the ratings cache) in structure and locking.
/// </summary>
public class MdbListListsCacheService
{
    private readonly string _cacheFilePath;
    private readonly ILogger<MdbListListsCacheService> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private const string CatalogKey = "catalog";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private ConcurrentDictionary<string, MdbListListsCacheEntry>? _cache;

    public MdbListListsCacheService(ILogger<MdbListListsCacheService> logger)
    {
        _logger = logger;
        var dataPath = MoonfinPlugin.Instance?.DataFolderPath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jellyfin", "plugins", "Moonfin");

        if (!Directory.Exists(dataPath))
        {
            Directory.CreateDirectory(dataPath);
        }

        _cacheFilePath = Path.Combine(dataPath, "mdblist_lists_cache.json");
    }

    public List<MdbListCatalogEntry>? TryGetCatalog(TimeSpan maxAge)
    {
        var cache = EnsureLoaded();
        if (cache.TryGetValue(CatalogKey, out var entry) &&
            DateTimeOffset.UtcNow - entry.CachedAt < maxAge)
        {
            return entry.Catalog;
        }
        return null;
    }

    public void SetCatalog(List<MdbListCatalogEntry> catalog)
    {
        var cache = EnsureLoaded();
        cache[CatalogKey] = new MdbListListsCacheEntry
        {
            Catalog = catalog,
            CachedAt = DateTimeOffset.UtcNow
        };
    }

    public List<MdbListItem>? TryGetItems(string slug, TimeSpan maxAge)
    {
        var cache = EnsureLoaded();
        if (cache.TryGetValue(ItemsKey(slug), out var entry) &&
            DateTimeOffset.UtcNow - entry.CachedAt < maxAge)
        {
            return entry.Items;
        }
        return null;
    }

    public void SetItems(string slug, List<MdbListItem> items)
    {
        var cache = EnsureLoaded();
        cache[ItemsKey(slug)] = new MdbListListsCacheEntry
        {
            Items = items,
            CachedAt = DateTimeOffset.UtcNow
        };
    }

    private static string ItemsKey(string slug) => $"items:{slug}";

    /// <summary>
    /// Returns already-resolved posters from the current cache, keyed by "{type}:{tmdbId}",
    /// so the sync only calls TMDB for ids it has not resolved before.
    /// </summary>
    public Dictionary<string, string> GetKnownPosters()
    {
        var cache = EnsureLoaded();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in cache.Values)
        {
            if (entry.Items == null) continue;
            foreach (var item in entry.Items)
            {
                var tmdb = item.ProviderIds?.Tmdb;
                if (string.IsNullOrEmpty(tmdb) || string.IsNullOrEmpty(item.Poster)) continue;
                var key = item.Type + ":" + tmdb;
                if (!result.ContainsKey(key)) result[key] = item.Poster!;
            }
        }
        return result;
    }

    public async Task FlushAsync()
    {
        var cache = _cache;
        if (cache == null) return;

        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var stream = File.Create(_cacheFilePath);
            await JsonSerializer.SerializeAsync(stream, cache, JsonOptions).ConfigureAwait(false);
            _logger.LogDebug("MDBList lists cache flushed to disk ({Count} entries)", cache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush MDBList lists cache to disk");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private ConcurrentDictionary<string, MdbListListsCacheEntry> EnsureLoaded()
    {
        if (_cache != null) return _cache;

        _fileLock.Wait();
        try
        {
            if (_cache != null) return _cache;

            if (File.Exists(_cacheFilePath))
            {
                try
                {
                    using var stream = File.OpenRead(_cacheFilePath);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, MdbListListsCacheEntry>>(stream, JsonOptions);
                    _cache = loaded != null
                        ? new ConcurrentDictionary<string, MdbListListsCacheEntry>(loaded, StringComparer.OrdinalIgnoreCase)
                        : new ConcurrentDictionary<string, MdbListListsCacheEntry>(StringComparer.OrdinalIgnoreCase);
                    _logger.LogInformation("MDBList lists cache loaded from disk ({Count} entries)", _cache.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load MDBList lists cache from disk, starting fresh");
                    _cache = new ConcurrentDictionary<string, MdbListListsCacheEntry>(StringComparer.OrdinalIgnoreCase);
                }
            }
            else
            {
                _cache = new ConcurrentDictionary<string, MdbListListsCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }
        finally
        {
            _fileLock.Release();
        }

        return _cache;
    }
}

internal class MdbListListsCacheEntry
{
    [JsonPropertyName("catalog")]
    public List<MdbListCatalogEntry>? Catalog { get; set; }

    [JsonPropertyName("items")]
    public List<MdbListItem>? Items { get; set; }

    [JsonPropertyName("cachedAt")]
    public DateTimeOffset CachedAt { get; set; }
}
