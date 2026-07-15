using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moonfin.Server.Services;

namespace Moonfin.Server.Api;

/// <summary>
/// API controller for Moonfin custom theme uploads and sync payload retrieval.
/// </summary>
[ApiController]
[Route("Moonfin")]
[Produces(MediaTypeNames.Application.Json)]
public class MoonfinThemesController : ControllerBase
{
    private readonly MoonfinThemeStore _themeStore;
    private readonly MoonfinSettingsService _settingsService;

    public MoonfinThemesController(MoonfinThemeStore themeStore, MoonfinSettingsService settingsService)
    {
        _themeStore = themeStore;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Returns all uploaded custom themes for authenticated clients.
    /// </summary>
    [HttpGet("Themes")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> GetThemes()
    {
        var config = MoonfinPlugin.Instance?.Configuration;
        if (config?.EnableSettingsSync != true)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Settings sync is disabled" });
        }

        var themes = await _themeStore.ListThemesAsync().ConfigureAwait(false);
        return Ok(themes);
    }

    /// <summary>
    /// Returns one uploaded custom theme by ID.
    /// </summary>
    [HttpGet("Themes/{themeId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> GetThemeById([FromRoute] string themeId)
    {
        var config = MoonfinPlugin.Instance?.Configuration;
        if (config?.EnableSettingsSync != true)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Settings sync is disabled" });
        }

        var theme = await _themeStore.GetThemeAsync(themeId).ConfigureAwait(false);
        if (!theme.HasValue)
        {
            return NotFound(new { error = "Theme not found" });
        }

        return Ok(theme.Value);
    }

    /// <summary>
    /// Returns uploaded theme metadata for the admin panel.
    /// </summary>
    [HttpGet("Admin/Themes")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetAdminThemes()
    {
        return Ok(new { items = _themeStore.GetThemeIndex() });
    }

    /// <summary>
    /// Uploads or replaces a custom theme JSON payload.
    /// </summary>
    [HttpPost("Admin/Themes")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> UploadTheme([FromBody] JsonElement themePayload)
    {
        if (themePayload.ValueKind != JsonValueKind.Object)
        {
            return BadRequest(new
            {
                error = "Theme payload must be a JSON object.",
                errors = new[] { "Theme payload must be a JSON object." }
            });
        }

        var userId = this.GetUserIdFromClaims();
        var saveResult = await _themeStore.SaveThemeAsync(themePayload, userId).ConfigureAwait(false);

        if (saveResult.Entry == null)
        {
            return BadRequest(new
            {
                error = "Theme validation failed.",
                errors = saveResult.Errors
            });
        }

        _settingsService.BroadcastSystemEvent("themesChanged");

        return Ok(new
        {
            success = true,
            item = saveResult.Entry
        });
    }

    /// <summary>
    /// Deletes one uploaded custom theme by ID.
    /// </summary>
    [HttpDelete("Admin/Themes/{themeId}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteTheme([FromRoute] string themeId)
    {
        var removed = await _themeStore.DeleteThemeAsync(themeId).ConfigureAwait(false);
        if (!removed)
        {
            return NotFound(new { error = "Theme not found" });
        }

        _settingsService.BroadcastSystemEvent("themesChanged");

        return Ok(new { success = true });
    }
}
