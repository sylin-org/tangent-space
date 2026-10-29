using Microsoft.AspNetCore.Mvc;

namespace Tangent.Render;

/// <summary>Every error leaves in this one shape, RFC 9457 problem details: <c>application/problem+json</c> for
/// JSON and the shell with the problem for HTML, by <c>Accept</c>. Where a verb is missing, <c>gate</c> is the one
/// reason it is missing. Sealed with plain members so Koan's field-access guard leaves the formatter choice alone.</summary>
public sealed record Problem(int Status, string Title, string? Detail = null, string? Gate = null)
{
    public ObjectResult ToResult() => new(this)
    {
        StatusCode = Status,
        ContentTypes = { "text/html", "application/problem+json" }
    };
}
