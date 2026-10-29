using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Replies in a Topic: <c>POST /t/{tangent}/{topic}</c>, beside the Topic's own page, takes <c>body</c>,
/// <c>parent</c> (a Post of the Topic; its root when none is named) and <c>id</c>, the reply's TID, as a form or a
/// JSON object, and sends <see cref="Reply"/>. The address is resolved as the Topic page resolves it
/// (<see cref="PostWrites.Locate"/>) before anything is judged of what was sent, so a Topic the sender may not see and
/// one no slug names answer alike. A form is sent on to the reply's address, a JSON request reads the Post
/// (<c>201</c>); a refusal is a problem with the gate, and the same id with other words is a conflict.</summary>
public sealed class ReplyController(KoanWebBaseUrl baseUrl, WriteLoop writer) : ControllerBase
{
    [HttpPost("/t/{tangent}/{topic}")]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public async Task<IActionResult> Write(string tangent, string topic, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var (found, unseen) = await PostWrites.Locate(personId, tangent, topic, null, ct);
        if (found is null) return unseen!;
        var root = found.Root!;
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var outcome = await writer.Send(new Reply(
            fields.GetValueOrDefault(PostWrites.IdField) ?? "",
            root.Id,
            PostWrites.Body(fields, found.Rows.For(root).Holds(Bit.Post)),
            personId ?? "",
            fields.GetValueOrDefault(PostWrites.ParentField) is { Length: > 0 } parent ? parent : null), ct);
        return outcome switch
        {
            Outcome<Post>.Done { Result: var reply } => await PostWrites.Written(HttpContext, reply, root,
                PostWrites.Address(Configured(), root.TangentId, root.Slug ?? root.Id, reply.Id), personId, created: true, ct),
            Outcome<Post>.Refused { Reason: Reply.NoTopic } => PostWrites.Absent(),
            Outcome<Post>.Refused { Reason: var reason } => PostWrites.Refused(reason),
            Outcome<Post>.Conflict { Reason: var reason } => PostWrites.Conflicting(reason),
            Outcome<Post>.Failed { Cause: var cause } => throw new InvalidOperationException($"Replying in {tangent}/{topic} failed.", cause),
            _ => throw new InvalidOperationException($"The writer answered {outcome} for a reply in {tangent}/{topic}.")
        };
    }

    private string Configured() => baseUrl.Configured
        ?? throw new InvalidOperationException("Koan:Web:ApplicationUrl must be set to the base URL the Tangent Space answers at.");
}
