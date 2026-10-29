using Microsoft.AspNetCore.Mvc;

namespace Tangent.Render;

/// <summary>Answers every address nothing else answers with a 404 problem, in the shape the request asked for.</summary>
public sealed class NotFoundController : ControllerBase
{
    [HttpGet("{**path}", Order = int.MaxValue)]
    public IActionResult Get()
        => new Problem(StatusCodes.Status404NotFound, "Not found", "Nothing answers at this address.").ToResult();
}
