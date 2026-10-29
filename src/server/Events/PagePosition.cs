using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Tangent.Events;

/// <summary>Where the log stood when a request began, for the page it renders: the live updater on that page streams
/// from here. The head is taken before the request reads anything (<see cref="Taken"/>), from the event stream's head,
/// which never runs ahead of what is committed, so every row the page could not have read is past it; a row the page
/// did read may come again, and the updater takes it as already shown. The page carries the newest row through that
/// head which reaches its reader (<see cref="Readable.Newest"/>), so it says nothing of rows in Tangents they may not
/// view; the rows between are hidden from them, and the stream passes over them.</summary>
public sealed class PagePosition
{
    private long? _head;
    private string? _participantId;

    /// <summary>Where the page's reader begins on the log, read once; null when the request took no head.</summary>
    public async Task<long?> Since(CancellationToken ct)
        => _head is { } head ? await Readable.Newest(_participantId, head, ct) : null;

    /// <summary>Takes the head as each action begins, before it reads, with who reads.</summary>
    public sealed class Taken(EventStream stream, PagePosition position) : IAsyncResourceFilter
    {
        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            var user = context.HttpContext.User;
            position._participantId = user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
            position._head = await stream.Head(context.HttpContext.RequestAborted);
            await next();
        }
    }
}
