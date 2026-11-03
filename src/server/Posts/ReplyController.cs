using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;

namespace Tangent.Posts;

/// <summary>What a reply sends, as a form or one JSON object.</summary>
public sealed record ReplyInput(
    [Description(Holds.ReplyBody)] string? Body = null,
    [Description(Holds.Parent)] string? Parent = null,
    [Description(Holds.Id)] string? Id = null);

/// <summary>Replies in a Topic: <c>POST /t/{tangent}/{topic}</c>, beside the Topic's own page, takes
/// <see cref="ReplyInput"/> and sends <see cref="Reply"/>. The address is resolved as the Topic page resolves it
/// (<see cref="PostWrites.Locate"/>) before anything is judged of what was sent, so a Topic the sender may not see and
/// one no slug names answer alike. A form is sent on to the reply's address, a JSON request reads the Post
/// (<c>201</c>); a refusal is a problem with the gate, and the same id with other words is a conflict.</summary>
public sealed class ReplyController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    [HttpPost("/t/{tangent}/{topic}")]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "reply", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Reply in a Topic you have read. parent is the id of the Post you answer; left out, you answer the Topic. Send an id, a TID you mint, and the same call again returns the same Post, so a retry is safe; without one, each call makes a new Post. Refused, its gate says why and its next, where there is one, what to do now.")]
    public async Task<IActionResult> Write([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        ReplyInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, topic, null, ct);
        if (found is null) return unseen!;
        var root = found.Root!;

        var body = await PostWrites.Body(input.Body, found.Rows.For(root).Holds(Bit.Post));
        var parent = input.Parent is { Length: > 0 } named ? named : null;
        var outcome = await new Reply(PostWrites.Id(input.Id), root.Id, body, personId ?? "", parent).Send();
        return outcome is Outcome<Post>.Done { Result: var reply }
            ? await PostWrites.Written(HttpContext, reply, root, PostWrites.Address(BaseAddress(), root.TangentId, root.Slug ?? root.Id, reply.Id), personId, created: true, ct)
            : PostWrites.Answer(HttpContext, outcome);
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
