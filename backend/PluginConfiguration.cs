using MediaBrowser.Model.Plugins;
using Moonfin.Server.Models;

namespace Moonfin.Server;

/// <summary>
/// Admin-level plugin configuration for Moonfin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Enable settings sync across Moonfin clients.
    /// </summary>
    public bool EnableSettingsSync { get; set; } = true;

    /// <summary>
    /// Enable Seerr integration for all users.
    /// </summary>
    public bool SeerrEnabled { get; set; } = false;

    /// <summary>
    /// Seerr server URL for server-to-server communication from Jellyfin.
    /// Example: http://seerr:5055 or http://192.168.50.20:5055
    /// </summary>
    public string? SeerrUrl { get; set; }

    /// <summary>
    /// Optional display name override (e.g., "Requests", "Media Requests").
    /// Leave empty to auto-detect based on server version.
    /// </summary>
    public string? SeerrDisplayName { get; set; }

    // Legacy keys from before the Jellyseerr -> Seerr rename. Kept only so existing
    // configs deserialize; MigrateLegacyKeys() copies them into the Seerr* keys on load.
    public string? JellyseerrUrl { get; set; }
    public bool JellyseerrEnabled { get; set; }
    public string? JellyseerrDisplayName { get; set; }

    /// <summary>
    /// Server-wide MDBList API key shared with all users.
    /// Users who set their own key will use that instead.
    /// </summary>
    public string? MdblistApiKey { get; set; }

    /// <summary>
    /// Server-wide TMDB API key shared with all users.
    /// Users who set their own key will use that instead.
    /// </summary>
    public string? TmdbApiKey { get; set; }

    /// <summary>
    /// Fetch and cache MDBList official lists (curated charts) on a schedule so clients
    /// can show them as home rows. Uses the server-wide MDBList key above.
    /// </summary>
    public bool MdblistOfficialListsEnabled { get; set; } = true;

    /// <summary>
    /// Maximum number of items cached per official list. Caps cache size and API calls
    /// (250 covers charts like IMDb Top 250).
    /// </summary>
    public int MdblistOfficialListsMaxItems { get; set; } = 250;

    /// <summary>
    /// Optional default server URL shown in the Moonfin web Add Server dialog.
    /// </summary>
    public string? WebDefaultServerUrl { get; set; }

    /// <summary>
    /// Optional forced server URL for Moonfin web plugin mode auto-connect.
    /// </summary>
    public string? WebForcedServerUrl { get; set; }

    /// <summary>
    /// Enable WebRTC private subnet scan when running Moonfin web plugin mode.
    /// </summary>
    public bool WebEnableWebRtcScan { get; set; } = true;

    /// <summary>
    /// Admin-configured default settings for all users.
    /// Users who haven't customized a setting will inherit this value.
    /// Users can override any default in their own Moonfin settings.
    /// </summary>
    public MoonfinSettingsProfile? DefaultUserSettings { get; set; }

    /// <summary>
    /// Metadata index for uploaded custom themes stored in the plugin data folder.
    /// </summary>
    public List<UploadedThemeEntry> UploadedThemes { get; set; } = new();

    /// <summary>
    /// Gets the effective Seerr URL for server-to-server communication.
    /// </summary>
    public string? GetEffectiveSeerrUrl()
    {
        return NormalizeSeerrUrl(SeerrUrl ?? JellyseerrUrl);
    }

    /// <summary>
    /// Copies any pre-rename Jellyseerr* values into the Seerr* keys and clears the
    /// legacy ones. Returns true when something changed so the caller can persist.
    /// </summary>
    public bool MigrateLegacyKeys()
    {
        var changed = false;

        if (string.IsNullOrEmpty(SeerrUrl) && !string.IsNullOrEmpty(JellyseerrUrl))
        {
            SeerrUrl = JellyseerrUrl;
            changed = true;
        }

        if (!SeerrEnabled && JellyseerrEnabled)
        {
            SeerrEnabled = true;
            changed = true;
        }

        if (string.IsNullOrEmpty(SeerrDisplayName) && !string.IsNullOrEmpty(JellyseerrDisplayName))
        {
            SeerrDisplayName = JellyseerrDisplayName;
            changed = true;
        }

        if (JellyseerrUrl != null || JellyseerrEnabled || JellyseerrDisplayName != null)
        {
            JellyseerrUrl = null;
            JellyseerrEnabled = false;
            JellyseerrDisplayName = null;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Normalizes a user-entered Seerr URL so downstream <see cref="Uri"/> parsing and
    /// string concatenation always receive a sane absolute http/https address.
    /// Trims whitespace and surrounding quotes, prepends <c>http://</c> when no scheme
    /// is present (otherwise <c>new Uri("seerr:5055")</c> treats "seerr" as the scheme),
    /// strips trailing slashes, and validates the result. Returns <c>null</c> when the
    /// value is empty or not a usable http/https URL.
    /// </summary>
    public static string? NormalizeSeerrUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var value = rawUrl.Trim().Trim('"', '\'').Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        value = value.TrimEnd('/');

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return value;
    }
}

/// <summary>
/// Metadata for an uploaded custom theme JSON file.
/// </summary>
public class UploadedThemeEntry
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; }
    public string? UploadedByUserId { get; set; }
    public string ChecksumSha256 { get; set; } = string.Empty;
}
