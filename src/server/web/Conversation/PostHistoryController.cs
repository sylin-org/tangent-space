using Koan.Web.Controllers;
using Koan.Web.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Tangent.Community;
using Tangent.Identity;

namespace Tangent.Conversation;

/// <summary>Read-only changelog for one authorized original Post. The caller supplies
/// <c>?set=changelog&amp;postId=...</c>; domain governance authorizes that Post before Koan applies
/// pagination and filters. <see cref="PostHistoryAccess"/> confines both collection and by-id
/// reads to that Post's snapshots. Only conversation domain operations may write history.</summary>
[ApiController, Authorize, Route("api/history/messages"), Pagination(DefaultSize = 1, MaxSize = 1)]
public sealed class PostHistoryController(TopicGovernance governance, PostHistoryScope scope) : EntityController<Post, string>
{
    protected override ObjectResult PrepareResponse(object? content)
        => base.PrepareResponse(content switch
        {
            Post post => PostReadProjection.Of(post),
            IEnumerable<Post> posts => posts.Select(PostReadProjection.Of).ToArray(),
            _ => content,
        });

    private IActionResult Denied(string reason) => StatusCode(403, new { reason });

    [HttpGet("")]
    public override Task<IActionResult> GetCollection(CancellationToken ct)
        => Serve(() => base.GetCollection(ct), ct);

    [HttpGet("{id}")]
    public override Task<IActionResult> GetById(string id, CancellationToken ct)
        => Serve(() => base.GetById(id, ct), ct);

    // Every remaining inherited action is refused. The route attributes are re-declared so the
    // denial does not depend on attribute inheritance across the virtual overrides.

    [HttpPost("query")]
    public override Task<IActionResult> Query(object body, CancellationToken ct)
        => Task.FromResult(Denied("History queries use the collection read with ?set=changelog&postId=... ."));

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

    private async Task<IActionResult> Serve(Func<Task<IActionResult>> read, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        // ?set= is framework-read (EntityContext.With(partition:)) — this check only pins the one
        // partition this surface may ever touch; multi-valued or absent sets never match exactly.
        var set = Request.Query["set"].ToString();
        if (!string.Equals(set, Post.ChangelogPartition, StringComparison.Ordinal))
            return Denied("The history surface serves the changelog set only.");
        var postId = Request.Query["postId"].ToString();
        if (string.IsNullOrWhiteSpace(postId) || postId.Length > 128)
            return BadRequest(new { reason = "Choose the original Post whose history you want to read." });
        string actor;
        try { actor = ParticipationAccess.Require(User, ParticipationGrants.Read); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        using (EntityContext.NoCache())
        {
            var post = await Post.Get(postId, ct);
            if (post is null || post.OfPostId is not null || !await governance.CanReadHistory(actor, post, ct))
                return NotFound();
        }
        scope.OriginalPostId = postId;
        try { return await read(); }
        finally { scope.OriginalPostId = null; }
    }
}
