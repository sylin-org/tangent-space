using Microsoft.AspNetCore.Mvc.Filters;

namespace Tangent.Render;

/// <summary>A JSON body past a route's limit, wherever binding met it, answers as what it is, as a tool's call of the same
/// size does, and not as a failure.</summary>
public sealed class TooLargeAnswer : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }) return;
        context.Result = new Problem(StatusCodes.Status413PayloadTooLarge, "Too large", "This is more than this address takes, so nothing was written.").ToResult();
        context.ExceptionHandled = true;
    }
}
