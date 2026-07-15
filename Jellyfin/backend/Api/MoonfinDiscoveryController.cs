using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Moonfin.Server.Api;

/// <summary>
/// Discovery endpoint used by Moonfin web plugin mode.
/// </summary>
[ApiController]
[Route("Moonfin/Discovery")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class MoonfinDiscoveryController : ControllerBase
{
    /// <summary>
    /// Returns same-origin Jellyfin server metadata for web discovery proxy mode.
    /// </summary>
    [HttpGet]
    [HttpGet("discover")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Discover()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        var address = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var servers = new[]
        {
            new
            {
                id = "jellyfin-local",
                name = "Jellyfin",
                address,
                type = "Jellyfin"
            }
        };

        return Ok(servers);
    }
}
