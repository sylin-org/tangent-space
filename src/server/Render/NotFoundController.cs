using Microsoft.AspNetCore.Mvc;
using Tangent.Posts;

namespace Tangent.Render;

/// <summary>Answers every address nothing else answers, whatever the method, with a 404 problem in the shape the
/// request asked for.</summary>
public sealed class NotFoundController : ControllerBase
{
    [Route("{**path}", Order = int.MaxValue)]
    public IActionResult Answer() => PostWrites.Absent();
}
