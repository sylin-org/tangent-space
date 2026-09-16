using Koan.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TangentSpace.Conversation;

/// <summary>The Post history surface (D3): the app's first generic entity read mount, serving
/// only the changelog partition and only reads. A read is honored only when it explicitly selects
/// <c>?set=changelog</c> — the default partition and every other set are denied here — and every
/// mutating action is denied outright because the changelog is write-once history mutated only by
/// domain code (ConversationService.ChangePost). Row visibility is not this controller's concern:
/// the <see cref="PostHistoryAccess"/> realization gates each snapshot to its author or a
/// moderation-capable viewer. POST /query is deliberately not served; the collection GET carries
/// the same JSON filter DSL, and an extra body-driven set path is one more thing to get wrong.</summary>
[ApiController, Authorize, Route("api/history/messages")]
public sealed class PostHistoryController : EntityController<Post, string>
{
    private IActionResult Denied(string reason) => StatusCode(403, new { reason });

    [HttpGet("")]
    public override Task<IActionResult> GetCollection(CancellationToken ct)
        => Serve(() => base.GetCollection(ct));

    [HttpGet("{id}")]
    public override Task<IActionResult> GetById(string id, CancellationToken ct)
        => Serve(() => base.GetById(id, ct));

    // Every remaining inherited action is refused. The route attributes are re-declared so the
    // denial does not depend on attribute inheritance across the virtual overrides.

    [HttpPost("query")]
    public override Task<IActionResult> Query(object body, CancellationToken ct)
        => Task.FromResult(Denied("History queries use the collection read with ?set=changelog."));

    [HttpGet("new")]
    public override Task<IActionResult> GetNew(CancellationToken ct)
        => Task.FromResult(Denied("The history surface serves recorded snapshots only."));

    [HttpPost("")]
    public override Task<IActionResult> Upsert(Post model, CancellationToken ct)
        => Task.FromResult(Denied("The changelog is append-only history and accepts no writes."));

    [HttpPut("{id}")]
    public override Task<IActionResult> Put(string id, CancellationToken ct)
        => Task.FromResult(Denied("The changelog is append-only history and accepts no writes."));

    [HttpPost("bulk")]
    public override Task<IActionResult> UpsertMany(IEnumerable<Post> models, CancellationToken ct)
        => Task.FromResult(Denied("The changelog is append-only history and accepts no writes."));

    [HttpDelete("{id}")]
    public override Task<IActionResult> Delete(string id, CancellationToken ct)
        => Task.FromResult(Denied("Snapshots are never removed."));

    [HttpDelete("bulk")]
    public override Task<IActionResult> DeleteMany(IEnumerable<string> ids, CancellationToken ct)
        => Task.FromResult(Denied("Snapshots are never removed."));

    [HttpDelete("")]
    public override Task<IActionResult> DeleteByQuery(string? q, CancellationToken ct)
        => Task.FromResult(Denied("Snapshots are never removed."));

    [HttpDelete("all")]
    public override Task<IActionResult> DeleteAll(CancellationToken ct)
        => Task.FromResult(Denied("Snapshots are never removed."));

    [HttpPatch("{id}")]
    public override Task<IActionResult> Patch(string id, CancellationToken ct)
        => Task.FromResult(Denied("Snapshots are write-once and cannot be patched."));

    private async Task<IActionResult> Serve(Func<Task<IActionResult>> read)
    {
        Response.Headers.CacheControl = "no-store";
        // ?set= is framework-read (EntityContext.With(partition:)) — this check only pins the one
        // partition this surface may ever touch; multi-valued or absent sets never match exactly.
        var set = Request.Query["set"].ToString();
        return string.Equals(set, Post.ChangelogPartition, StringComparison.Ordinal)
            ? await read()
            : Denied("The history surface serves the changelog set only.");
    }
}
