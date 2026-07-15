using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moonfin.Server.Services;

namespace Moonfin.Server.Api;

/// <summary>
/// Per-user custom ordering of a collection's items. The Moonfin client uses this to persist a
/// drag-and-drop order that only affects what that one user sees, stored server-side so it
/// follows them across devices.
/// </summary>
[ApiController]
[Route("Moonfin/Collections")]
public class CollectionsController : ControllerBase
{
    private readonly CollectionOrderService _orderService;

    public CollectionsController(CollectionOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>Returns the current user's order for a collection, or an empty array when none is set.</summary>
    [HttpGet("{collectionId}/Order")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<string>>> GetOrder(
        [FromRoute] string collectionId,
        CancellationToken cancellationToken)
    {
        var userId = this.GetUserIdFromClaims();
        if (userId == null)
        {
            return Unauthorized();
        }

        var order = await _orderService.GetAsync(userId.Value, collectionId, cancellationToken).ConfigureAwait(false);
        return Ok(order);
    }

    /// <summary>Saves the current user's order for a collection. An empty array clears it.</summary>
    [HttpPost("{collectionId}/Order")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SaveOrder(
        [FromRoute] string collectionId,
        [FromBody] List<string>? itemIds,
        CancellationToken cancellationToken)
    {
        var userId = this.GetUserIdFromClaims();
        if (userId == null)
        {
            return Unauthorized();
        }

        await _orderService.SaveAsync(userId.Value, collectionId, itemIds ?? new List<string>(), cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
