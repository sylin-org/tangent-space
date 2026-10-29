using Microsoft.AspNetCore.Mvc;

namespace Tangent.Space;

/// <summary>The descriptor: one bounded read of the one row, as JSON.</summary>
public sealed class DescriptorController : ControllerBase
{
    [HttpGet("/tangent.json")]
    public async Task<ActionResult<Descriptor>> Get(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct)
            ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        return new Descriptor(space.Name);
    }
}
