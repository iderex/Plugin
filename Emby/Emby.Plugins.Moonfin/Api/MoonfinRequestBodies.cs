using System.Text.Json.Serialization;
using Emby.Plugins.Moonfin.Models;

namespace Emby.Plugins.Moonfin.Api
{
    internal class SaveSettingsBody
    {
        [JsonPropertyName("settings")] public MoonfinUserSettings? Settings { get; set; }
        [JsonPropertyName("clientId")] public string? ClientId { get; set; }
        [JsonPropertyName("mergeMode")] public string? MergeMode { get; set; }
    }

    internal class SaveProfileBody
    {
        [JsonPropertyName("profile")] public MoonfinSettingsProfile? Profile { get; set; }
        [JsonPropertyName("clientId")] public string? ClientId { get; set; }
    }

    internal class SaveDetailsBlurBody
    {
        [JsonPropertyName("profile")] public string? Profile { get; set; }
        [JsonPropertyName("detailsScreenBlur")] public string? DetailsScreenBlur { get; set; }
        [JsonPropertyName("clientId")] public string? ClientId { get; set; }
    }

    internal class SaveDetailsOpacityBody
    {
        [JsonPropertyName("profile")] public string? Profile { get; set; }
        [JsonPropertyName("detailsScreenOpacity")] public int? DetailsScreenOpacity { get; set; }
        [JsonPropertyName("clientId")] public string? ClientId { get; set; }
    }

    internal class BroadcastBody
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
