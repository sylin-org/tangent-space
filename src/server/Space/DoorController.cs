using Microsoft.AspNetCore.Mvc;

namespace Tangent.Space;

/// <summary>The door: one bounded read of the one row, left to the formatter as HTML, a fragment or JSON.</summary>
public sealed class DoorController : ControllerBase
{
    [HttpGet("/")]
    [HttpHead("/")]
    public async Task<ActionResult<Door>> Get(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct)
            ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        return new Door(space.Name, space.BaseUrl, space.Version);
    }
}
