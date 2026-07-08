using System.Collections.Concurrent;
using System.Text.Json;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Moonfin.Server.Services;

/// <summary>
/// Turns Seerr webhook payloads into per-user Moonfin notifications and delivers
/// them over the SSE stream, then also as FCM push for backgrounded clients.
/// </summary>
public class SeerrWebhookService
{
    private const int ManageRequestsBit = 16;
    private const int AdminBit = 2;
    private const int OwnerSeerrUserId = 1;

    private readonly SeerrSessionService _sessionService;
    private readonly MoonfinSettingsService _settingsService;
    private readonly NotificationStore _store;
    private readonly FcmSender _fcmSender;
    private readonly RelaySender _relaySender;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SeerrWebhookService> _logger;

    private readonly ConcurrentDictionary<string, long> _recent = new();
    private static readonly long DebounceWindowMs = 60_000;

    public SeerrWebhookService(
        SeerrSessionService sessionService,
        MoonfinSettingsService settingsService,
        NotificationStore store,
        FcmSender fcmSender,
        RelaySender relaySender,
        ILibraryManager libraryManager,
        ILogger<SeerrWebhookService> logger)
    {
        _sessionService = sessionService;
        _settingsService = settingsService;
        _store = store;
        _fcmSender = fcmSender;
        _relaySender = relaySender;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public async Task HandleWebhookAsync(JsonElement payload)
    {
        try
        {
            var notificationType = GetString(payload, "notification_type");
            if (string.IsNullOrEmpty(notificationType))
            {
                return;
            }

            switch (notificationType.ToUpperInvariant())
            {
                case "MEDIA_PENDING":
                    await HandlePendingAsync(payload);
                    break;
                case "MEDIA_AVAILABLE":
                    await HandleAvailableAsync(payload);
                    break;
                default:
                    _logger.LogDebug("Ignoring Seerr webhook type {Type}", notificationType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle Seerr webhook");
        }
    }

    private Task HandlePendingAsync(JsonElement payload)
    {
        var subject = GetString(payload, "subject") ?? "a title";
        var requester = GetRequester(payload);
        var (tmdbId, _, mediaType) = GetMedia(payload);
        if (string.IsNullOrEmpty(tmdbId))
        {
            return Task.CompletedTask;
        }

        var route = $"/seerr/media/{tmdbId}?mediaType={NormalizeMediaType(mediaType)}";
        var title = "New request";
        var body = $"{requester ?? "Someone"} requested {subject}";

        var requesterJellyfinId = GetRequesterJellyfinId(payload);

        foreach (var userId in _store.GetUsersWantingNewRequests())
        {
            if (requesterJellyfinId.HasValue && userId == requesterJellyfinId.Value)
            {
                continue;
            }

            var session = _sessionService.EnumerateSessions()
                .FirstOrDefault(s => s.JellyfinUserId == userId);
            if (session == null || !CanManageRequests(session))
            {
                continue;
            }

            Deliver(userId, tmdbId, "MEDIA_PENDING", title, body, route);
        }

        return Task.CompletedTask;
    }

    private Task HandleAvailableAsync(JsonElement payload)
    {
        var subject = GetString(payload, "subject") ?? "Your request";
        var (tmdbId, tvdbId, mediaType) = GetMedia(payload);

        var targetUserId = GetRequesterJellyfinId(payload);
        if (targetUserId == null)
        {
            return Task.CompletedTask;
        }

        var prefs = _store.GetPrefs(targetUserId.Value);
        if (!prefs.NotifyOnLibraryAdded)
        {
            return Task.CompletedTask;
        }

        var jellyfinItemId = ResolveLibraryItemId(tmdbId, tvdbId);
        var route = jellyfinItemId != null
            ? $"/item/{jellyfinItemId}"
            : $"/seerr/media/{tmdbId}?mediaType={NormalizeMediaType(mediaType)}";

        var season = GetSeason(payload);
        var body = season.HasValue
            ? $"Season {season.Value} of {subject} is now available"
            : $"{subject} is now available";

        Deliver(targetUserId.Value, tmdbId ?? tvdbId ?? subject, "MEDIA_AVAILABLE", "Now available", body, route);
        return Task.CompletedTask;
    }

    private void Deliver(Guid userId, string mediaKey, string notificationType, string title, string body, string route)
    {
        var dedupeKey = $"{userId}:{mediaKey}:{notificationType}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (_recent.TryGetValue(dedupeKey, out var last) && now - last < DebounceWindowMs)
        {
            return;
        }

        _recent[dedupeKey] = now;
        PruneRecent(now);

        var json = JsonSerializer.Serialize(new
        {
            type = "seerrNotification",
            title,
            body,
            route
        });

        _settingsService.NotifyUser(userId, json);
        DeliverPush(userId, title, body, route);
    }

    // Sends the notification as push to every registered device for the user, so backgrounded or
    // closed clients still get it. Runs off the SSE path so a push failure never blocks or breaks
    // SSE delivery. The relay is the default (no service account needed); a self-hoster who
    // configures their own service account gets direct FCM instead. Dead tokens are pruned.
    private void DeliverPush(Guid userId, string title, string body, string route)
    {
        var config = MoonfinPlugin.Instance?.Configuration;
        if (config == null || !config.PushEnabled)
        {
            return;
        }

        var devices = _store.GetUserDevices(userId);
        if (devices.Count == 0)
        {
            return;
        }

        var tokens = devices
            .Select(d => d.Token)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        if (tokens.Count == 0)
        {
            return;
        }

        // A configured service account is an explicit self-hosted opt-in to direct FCM; otherwise
        // fall back to the hosted relay using the effective app key.
        if (config.HasServiceAccount)
        {
            _ = Task.Run(async () =>
            {
                foreach (var token in tokens)
                {
                    try
                    {
                        var result = await _fcmSender.SendAsync(token, title, body, route);
                        if (result == FcmSendResult.TokenDead)
                        {
                            _store.RemoveDeviceByToken(userId, token);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Push delivery failed for user {UserId}", userId);
                    }
                }
            });
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var dead = await _relaySender.SendAsync(tokens, title, body, route);
                foreach (var result in dead)
                {
                    _store.RemoveDeviceByToken(userId, result.Token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Relay push delivery failed for user {UserId}", userId);
            }
        });
    }

    private void PruneRecent(long now)
    {
        foreach (var pair in _recent)
        {
            if (now - pair.Value > DebounceWindowMs)
            {
                _recent.TryRemove(pair.Key, out _);
            }
        }
    }

    private static bool CanManageRequests(SeerrSession session)
    {
        if (session.SeerrUserId == OwnerSeerrUserId)
        {
            return true;
        }

        return (session.Permissions & AdminBit) != 0 || (session.Permissions & ManageRequestsBit) != 0;
    }

    private string? ResolveLibraryItemId(string? tmdbId, string? tvdbId)
    {
        var byTmdb = FindByProviderId(MetadataProvider.Tmdb, tmdbId);
        if (byTmdb != null)
        {
            return byTmdb;
        }

        return FindByProviderId(MetadataProvider.Tvdb, tvdbId);
    }

    private string? FindByProviderId(MetadataProvider provider, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var query = new InternalItemsQuery
            {
                HasAnyProviderId = new Dictionary<string, string>
                {
                    [provider.ToString()] = value
                },
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                IsVirtualItem = false,
                Recursive = true,
                Limit = 1
            };

            var item = _libraryManager.GetItemList(query).FirstOrDefault();
            return item?.Id.ToString("N");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Provider-id lookup failed for {Provider}={Value}", provider, value);
            return null;
        }
    }

    private Guid? GetRequesterJellyfinId(JsonElement payload)
    {
        var hasRequest = payload.TryGetProperty("request", out var request) &&
            request.ValueKind == JsonValueKind.Object;

        // Primary: Seerr's KeyMap exposes the requester's Jellyfin user GUID directly, so
        // no session lookup is needed. It may be in 32-char "N" or dashed "D" form.
        if (hasRequest)
        {
            var jellyfinGuid = GetString(request, "requestedBy_jellyfinUserId");
            if (!string.IsNullOrEmpty(jellyfinGuid) && Guid.TryParse(jellyfinGuid, out var parsed))
            {
                return parsed;
            }
        }

        // Fallback: map the requester's username against a stored session.
        if (hasRequest)
        {
            var username = GetString(request, "requestedBy_username");
            var byUsername = _sessionService.GetJellyfinUserForSeerrUsername(username);
            if (byUsername != null)
            {
                return byUsername;
            }
        }

        // Last resort: the numeric Seerr user id (not emitted by default).
        if (hasRequest && TryGetInt(request, "requestedBy_id", out var requestedById))
        {
            var mapped = _sessionService.GetJellyfinUserForSeerrUser(requestedById);
            if (mapped != null)
            {
                return mapped;
            }
        }

        if (TryGetInt(payload, "notifyuser_id", out var notifyUserId))
        {
            return _sessionService.GetJellyfinUserForSeerrUser(notifyUserId);
        }

        return null;
    }

    private static string? GetRequester(JsonElement payload)
    {
        if (payload.TryGetProperty("request", out var request) &&
            request.ValueKind == JsonValueKind.Object)
        {
            var name = GetString(request, "requestedBy_username")
                ?? GetString(request, "requestedBy_displayname");
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }

        return GetString(payload, "notifyuser_username");
    }

    private static (string? TmdbId, string? TvdbId, string? MediaType) GetMedia(JsonElement payload)
    {
        if (payload.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
        {
            return (
                GetString(media, "tmdbId"),
                GetString(media, "tvdbId"),
                GetString(media, "media_type"));
        }

        return (null, null, null);
    }

    private static int? GetSeason(JsonElement payload)
    {
        if (payload.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in extra.EnumerateArray())
            {
                var name = GetString(entry, "name");
                if (string.Equals(name, "Requested Seasons", StringComparison.OrdinalIgnoreCase))
                {
                    var value = GetString(entry, "value");
                    if (int.TryParse(value, out var season))
                    {
                        return season;
                    }
                }
            }
        }

        return null;
    }

    private static string NormalizeMediaType(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType))
        {
            return "movie";
        }

        return mediaType.ToLowerInvariant() switch
        {
            "tv" or "show" or "series" => "tv",
            _ => "movie"
        };
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Number => prop.ToString(),
                _ => null
            };
        }

        return null;
    }

    private static bool TryGetInt(JsonElement element, string name, out int value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var prop))
        {
            return false;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
        {
            return true;
        }

        return prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value);
    }
}
