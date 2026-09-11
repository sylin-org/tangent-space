using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TangentSpace.Web;

/// <summary>Explicit page routes share the client shell; data stays behind the domain API.</summary>
[AllowAnonymous]
public sealed class PagesController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("/onboarding/")]
    [HttpGet("/sign-in/")]
    [HttpGet("/tangents/")]
    [HttpGet("/t/{tangent}/topics")]
    [HttpGet("/t/{tangent}/topics/{topic}")]
    [HttpGet("/participants/{did}")]
    [HttpGet("/t/{tangent}/{post}")]
    public IActionResult Page()
    {
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(Path.Combine(environment.WebRootPath, "index.html"), "text/html; charset=utf-8");
    }
}
