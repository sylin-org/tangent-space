using Microsoft.AspNetCore.Mvc.Filters;

namespace Tangent.Events;

/// <summary>Where the log stood when a request began, for the page it renders: the live updater on that page streams
/// from here. It is taken before the request reads anything (<see cref="Taken"/>), from the event stream's head, which
/// never runs ahead of what is committed, so every row the page could not have read is past it; a row the page did
/// read may come again, and the updater takes it as already shown.</summary>
public sealed class PagePosition
{
    /// <summary>The log's last sequence as the request began; null until <see cref="Taken"/> has run.</summary>
    public long? Since { get; private set; }

    /// <summary>Takes the position as each action begins, before it reads.</summary>
    public sealed class Taken(EventStream stream, PagePosition position) : IAsyncResourceFilter
    {
        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            position.Since = await stream.Head(context.HttpContext.RequestAborted);
            await next();
        }
    }
}
