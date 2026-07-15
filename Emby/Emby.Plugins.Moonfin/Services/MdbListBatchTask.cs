using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.Plugins.Moonfin.Services
{
    public class MdbListBatchTask : IScheduledTask
    {
        public string Name => "Moonfin MDBList Ratings Sync";
        public string Key => "Moonfin.MdbList.BatchSync";
        public string Description => "Batch-fetches MDBList ratings for all movies and shows in the library.";
        public string Category => "Moonfin";

        private const int ApiBatchSize = 100;
        private const int LibraryPageSize = 2000;
        private const int DelayBetweenApiBatchesMs = 2000;
        private const int FlushEveryNBatches = 20;
        private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(7);

        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _logger;

        // Resolved lazily: Emby constructs IScheduledTask instances at plugin-load time, before
        // ServerEntryPoint.Run() initializes the service singletons, so this cannot be read in the ctor.
        private MdbListCacheService CacheService => Plugin.Instance?.MdbListCache
            ?? throw new InvalidOperationException("MdbListCacheService not initialized");

        public MdbListBatchTask(ILibraryManager libraryManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _logger = logManager.GetLogger("MoonfinMdbListBatch");
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            string? apiKey = null;
            try { apiKey = Plugin.Instance?.Configuration?.MdblistApiKey; }
            catch { /* configuration not ready yet (e.g. startup trigger before init) */ }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.Info("MDBList batch sync skipped: no server-wide API key configured", 0);
                return;
            }

            _logger.Info("MDBList batch sync starting...", 0);
            progress.Report(0);

            var freshKeys = CacheService.GetFreshKeys(CacheMaxAge);
            var uncachedItems = new List<LibraryItemInfo>();
            var startIndex = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = GetLibraryItemsPage(startIndex, LibraryPageSize);
                if (page.Count == 0) break;

                foreach (var item in page)
                    if (!freshKeys.Contains(item.CacheKey))
                        uncachedItems.Add(item);

                startIndex += page.Count;
                if (page.Count < LibraryPageSize) break;
            }

            if (uncachedItems.Count == 0)
            {
                _logger.Info("MDBList batch sync complete: all items already cached", 0);
                progress.Report(100);
                return;
            }

            var movieItems = uncachedItems.Where(i => i.Type == "movie").ToList();
            var showItems = uncachedItems.Where(i => i.Type == "show").ToList();
            var totalItems = movieItems.Count + showItems.Count;
            var processedItems = 0;

            processedItems = await FetchBatchesAsync(movieItems, "movie", apiKey, processedItems, totalItems, progress, cancellationToken).ConfigureAwait(false);
            await FetchBatchesAsync(showItems, "show", apiKey, processedItems, totalItems, progress, cancellationToken).ConfigureAwait(false);

            await CacheService.FlushAsync().ConfigureAwait(false);
            progress.Report(100);
        }

        private List<LibraryItemInfo> GetLibraryItemsPage(int startIndex, int limit)
        {
            var items = new List<LibraryItemInfo>();

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { "Movie", "Series" },
                IsVirtualItem = false,
                Recursive = true,
                StartIndex = startIndex,
                Limit = limit
            };

            var results = _libraryManager.GetItemsResult(query);
            foreach (var item in results.Items)
            {
                string? tmdbId = null;
                item.ProviderIds?.TryGetValue("Tmdb", out tmdbId);
                if (string.IsNullOrEmpty(tmdbId)) continue;

                var type = item.GetType().Name == "Movie" ? "movie" : "show";
                items.Add(new LibraryItemInfo
                {
                    TmdbId = tmdbId,
                    Type = type,
                    CacheKey = $"{type}:{tmdbId}"
                });
            }
            return items;
        }

        private async Task<int> FetchBatchesAsync(List<LibraryItemInfo> items, string type, string apiKey,
            int processedSoFar, int totalItems, IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (items.Count == 0) return processedSoFar;

            var batches = ChunkList(items, ApiBatchSize);
            var batchesSinceFlush = 0;

            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var tmdbIds = batch.Select(i => i.TmdbId).ToList();
                    var ratings = await FetchBatchFromApiAsync(type, tmdbIds, apiKey, cancellationToken).ConfigureAwait(false);
                    if (ratings != null) CacheService.SetMany(ratings);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger.Warn("Batch fetch failed for " + type + " batch, continuing: " + ex.Message);
                }

                processedSoFar += batch.Count;
                batchesSinceFlush++;
                if (totalItems > 0) progress.Report((double)processedSoFar / totalItems * 100);

                if (batchesSinceFlush >= FlushEveryNBatches)
                {
                    await CacheService.FlushAsync().ConfigureAwait(false);
                    batchesSinceFlush = 0;
                }

                if (processedSoFar < totalItems)
                    await Task.Delay(DelayBetweenApiBatchesMs, cancellationToken).ConfigureAwait(false);
            }

            return processedSoFar;
        }

        private async Task<Dictionary<string, List<MdbListRating>>?> FetchBatchFromApiAsync(
            string type, List<string> tmdbIds, string apiKey, CancellationToken cancellationToken)
        {
            var url = $"https://api.mdblist.com/tmdb/{Uri.EscapeDataString(type)}?apikey={Uri.EscapeDataString(apiKey)}";
            var requestBody = JsonSerializer.Serialize(new { ids = tmdbIds });

            using var client = MoonfinHttp.CreateClient(TimeSpan.FromSeconds(60), "Moonfin/1.0");

            using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode == 429)
            {
                _logger.Warn("MDBList rate limit hit during batch fetch");
                return null;
            }
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var batchResponse = JsonSerializer.Deserialize<List<MdbListBatchItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (batchResponse == null) return null;

            var result = new Dictionary<string, List<MdbListRating>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in batchResponse)
            {
                var tmdbId = item.Ids?.Tmdb?.ToString();
                if (string.IsNullOrEmpty(tmdbId)) continue;
                result[$"{type}:{tmdbId}"] = item.Ratings ?? new List<MdbListRating>();
            }
            return result;
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerStartup };
            yield return new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerDaily, TimeOfDayTicks = TimeSpan.FromHours(3).Ticks };
        }

        private static List<List<T>> ChunkList<T>(List<T> source, int chunkSize)
        {
            var chunks = new List<List<T>>();
            for (int i = 0; i < source.Count; i += chunkSize)
                chunks.Add(source.GetRange(i, Math.Min(chunkSize, source.Count - i)));
            return chunks;
        }

        private class LibraryItemInfo
        {
            public string TmdbId { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string CacheKey { get; set; } = string.Empty;
        }

        private class MdbListBatchItem
        {
            [JsonPropertyName("ids")] public MdbListBatchIds? Ids { get; set; }
            [JsonPropertyName("ratings")] public List<MdbListRating>? Ratings { get; set; }
        }

        private class MdbListBatchIds
        {
            [JsonPropertyName("tmdb")] public long? Tmdb { get; set; }
        }
    }
}
