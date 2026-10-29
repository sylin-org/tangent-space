using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;

namespace Tangent.Render;

/// <summary>An exception nothing caught leaves as a 500 problem in the shape the request asked for, through the
/// same formatters as every other response. The exception itself goes to the log, never to the client.</summary>
public static class ProblemExceptionHandler
{
    public static Task Answer(HttpContext http)
    {
        var result = new Problem(StatusCodes.Status500InternalServerError, "Something went wrong",
            "The Tangent Space could not answer this request.").ToResult();
        return result.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));
    }
}
