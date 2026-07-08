using System.Net;
using System.Text.Json;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Moonfin.Server.Services;

/// <summary>
/// Automatically registers Moonfin's webhook in Seerr using a stored admin session,
/// so admins don't have to paste the URL by hand. Guardrailed: it never overwrites a
/// third-party webhook, and it's idempotent. The manual WebhookInfo endpoint remains
/// the fallback whenever auto-provisioning can't run.
/// </summary>
public class SeerrProvisioningService
{
    private const int AdminBit = 2;
    private const int OwnerSeerrUserId = 1;

    // MEDIA_PENDING (2) | MEDIA_AVAILABLE (8).
    private const int TargetTypes = 10;

    private const string WebhookPath = "/Moonfin/Seerr/Webhook";
    private const string SettingsPath = "settings/notifications/webhook";

    // The template Seerr stores and expands per event. Double-brace keys are Seerr's
    // conditional-inclusion syntax (it replaces {{media}} -> media and strips the block
    // when absent), so the stored string is always valid JSON when our parser reads it.
    private const string PayloadTemplate = @"{
    ""notification_type"": ""{{notification_type}}"",
    ""subject"": ""{{subject}}"",
    ""message"": ""{{message}}"",
    ""{{media}}"": {
        ""media_type"": ""{{media_type}}"",
        ""tmdbId"": ""{{media_tmdbid}}"",
        ""tvdbId"": ""{{media_tvdbid}}"",
        ""status"": ""{{media_status}}""
    },
    ""{{request}}"": {
        ""request_id"": ""{{request_id}}"",
        ""requestedBy_username"": ""{{requestedBy_username}}"",
        ""requestedBy_jellyfinUserId"": ""{{requestedBy_jellyfinUserId}}""
    },
    ""{{extra}}"": []
}";

    private readonly SeerrSessionService _sessionService;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<SeerrProvisioningService> _logger;

    // Best-effort throttle so a busy login path doesn't hammer Seerr's settings API.
    private static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(5);
    private long _lastAttemptTicks;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public SeerrProvisioningService(
        SeerrSessionService sessionService,
        IServerApplicationHost appHost,
        ILogger<SeerrProvisioningService> logger)
    {
        _sessionService = sessionService;
        _appHost = appHost;
        _logger = logger;
    }

    /// <summary>The status of the last provisioning attempt, for surfacing to admins.</summary>
    public ProvisioningStatus LastStatus { get; private set; } = ProvisioningStatus.NotAttempted;

    /// <summary>
    /// Runs provisioning if it hasn't been tried within the retry window. Fire-and-forget safe.
    /// </summary>
    public async Task<ProvisioningResult> EnsureWebhookAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastAttemptTicks);
        if (last != 0 && now - last < RetryWindow.Ticks)
        {
            return new ProvisioningResult(LastStatus, "Skipped (tried recently).");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the gate so concurrent callers collapse into one attempt.
            last = Interlocked.Read(ref _lastAttemptTicks);
            if (last != 0 && now - last < RetryWindow.Ticks)
            {
                return new ProvisioningResult(LastStatus, "Skipped (tried recently).");
            }

            Interlocked.Exchange(ref _lastAttemptTicks, now);
            var result = await ProvisionAsync(cancellationToken);
            LastStatus = result.Status;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ProvisioningResult> ProvisionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = MoonfinPlugin.Instance?.Configuration;
            if (config == null || !config.SeerrEnabled || string.IsNullOrEmpty(config.GetEffectiveSeerrUrl()))
            {
                return new ProvisioningResult(ProvisioningStatus.NotConfigured, "Seerr is not enabled or configured.");
            }

            var admin = _sessionService.EnumerateSessions().FirstOrDefault(IsAdmin);
            if (admin == null)
            {
                return new ProvisioningResult(ProvisioningStatus.NoAdminSession,
                    "No admin Seerr session available yet. Configure the webhook manually or sign in as a Seerr admin.");
            }

            var secret = config.SeerrWebhookSecret ?? string.Empty;
            if (string.IsNullOrEmpty(secret))
            {
                return new ProvisioningResult(ProvisioningStatus.Failed, "Webhook secret is not set.");
            }

            var baseUrl = ResolvePublicBaseUrl(config.PublicServerUrl);
            if (string.IsNullOrEmpty(baseUrl))
            {
                return new ProvisioningResult(ProvisioningStatus.NeedsPublicUrl,
                    "Could not determine a public server URL. Set PublicServerUrl in the plugin config.");
            }

            var targetUrl = $"{baseUrl}{WebhookPath}?secret={Uri.EscapeDataString(secret)}";

            // Fetch the current settings via the admin session.
            var getResponse = await _sessionService.RequestWithSessionAsync(
                admin, HttpMethod.Get, SettingsPath, cancellationToken: cancellationToken);

            if (getResponse.StatusCode == 403)
            {
                return new ProvisioningResult(ProvisioningStatus.NoAdminSession,
                    "Stored session lacks admin rights on Seerr.");
            }

            if (getResponse.StatusCode is < 200 or >= 300 || getResponse.Body == null)
            {
                return new ProvisioningResult(ProvisioningStatus.Failed,
                    $"Reading webhook settings failed ({getResponse.StatusCode}).");
            }

            string? existingUrl = null;
            int existingTypes = 0;
            bool existingEnabled = false;
            string? existingPayload = null;
            try
            {
                using var doc = JsonDocument.Parse(getResponse.Body);
                var root = doc.RootElement;
                if (root.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.True)
                {
                    existingEnabled = true;
                }
                if (root.TryGetProperty("types", out var ty) && ty.ValueKind == JsonValueKind.Number)
                {
                    ty.TryGetInt32(out existingTypes);
                }
                if (root.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Object)
                {
                    if (opts.TryGetProperty("webhookUrl", out var wu) && wu.ValueKind == JsonValueKind.String)
                    {
                        existingUrl = wu.GetString();
                    }
                    if (opts.TryGetProperty("jsonPayload", out var jp) && jp.ValueKind == JsonValueKind.String)
                    {
                        existingPayload = jp.GetString();
                    }
                }
            }
            catch (JsonException)
            {
                // Treat an unreadable body as empty and continue to (re)write below.
            }

            // Guardrail: only touch an empty slot or our own webhook.
            var isOurs = !string.IsNullOrEmpty(existingUrl) &&
                existingUrl.Contains(WebhookPath, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(existingUrl) && !isOurs)
            {
                _logger.LogWarning(
                    "Seerr already has a different webhook configured ({Url}); not overwriting. " +
                    "Configure Moonfin's webhook manually (see the WebhookInfo endpoint).", existingUrl);
                return new ProvisioningResult(ProvisioningStatus.ForeignWebhookPresent,
                    "A different webhook is already configured in Seerr. Configure Moonfin's manually.");
            }

            // Idempotent: same url, enabled, our bits already set, same payload.
            if (isOurs && existingEnabled &&
                (existingTypes & TargetTypes) == TargetTypes &&
                string.Equals(existingUrl, targetUrl, StringComparison.Ordinal) &&
                string.Equals(existingPayload, PayloadTemplate, StringComparison.Ordinal))
            {
                return new ProvisioningResult(ProvisioningStatus.AlreadyProvisioned,
                    "Webhook already registered.");
            }

            var body = new
            {
                enabled = true,
                types = existingTypes | TargetTypes,
                options = new
                {
                    webhookUrl = targetUrl,
                    jsonPayload = PayloadTemplate,
                    authHeader = string.Empty
                }
            };

            var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
            var postResponse = await _sessionService.RequestWithSessionAsync(
                admin, HttpMethod.Post, SettingsPath, bytes, "application/json", cancellationToken);

            if (postResponse.StatusCode is >= 200 and < 300)
            {
                // Log without the query string so the webhook secret never lands in logs.
                _logger.LogInformation("Registered Moonfin webhook in Seerr at {Url}", baseUrl + WebhookPath);
                return new ProvisioningResult(ProvisioningStatus.Provisioned, "Webhook registered.");
            }

            return new ProvisioningResult(ProvisioningStatus.Failed,
                $"Writing webhook settings failed ({postResponse.StatusCode}).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Seerr webhook auto-provisioning failed");
            return new ProvisioningResult(ProvisioningStatus.Failed, ex.Message);
        }
    }

    private static bool IsAdmin(SeerrSession session) =>
        session.SeerrUserId == OwnerSeerrUserId || (session.Permissions & AdminBit) != 0;

    // PublicServerUrl wins; otherwise ask Jellyfin for its own address. GetSmartApiUrl
    // returns the configured published-server URL when set, else a loopback-derived one.
    private string? ResolvePublicBaseUrl(string? configuredUrl)
    {
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            return configuredUrl.TrimEnd('/');
        }

        try
        {
            var url = _appHost.GetSmartApiUrl(IPAddress.Loopback);
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url.TrimEnd('/');
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve the server's own API URL");
        }

        return null;
    }
}

/// <summary>Outcome of a provisioning attempt.</summary>
public enum ProvisioningStatus
{
    NotAttempted,
    NotConfigured,
    NoAdminSession,
    NeedsPublicUrl,
    ForeignWebhookPresent,
    AlreadyProvisioned,
    Provisioned,
    Failed
}

/// <summary>Result of a provisioning attempt with a human-readable detail.</summary>
public class ProvisioningResult
{
    public ProvisioningResult(ProvisioningStatus status, string message)
    {
        Status = status;
        Message = message;
    }

    public ProvisioningStatus Status { get; }

    public string Message { get; }
}
