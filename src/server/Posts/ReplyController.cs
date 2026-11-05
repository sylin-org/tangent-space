using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
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
/// <see cref="ReplyInput"/> and sends <see cref="Reply"/>; at a Post's address, <c>/t/{tangent}/{topic}/{post}</c>, the
/// reply answers that Post unless it names its parent, so every address a page or a write answers with replies there. It
/// needs <see cref="Bit.Post"/> on what the address names, which the Doorway judges before anything is bound
/// (<see cref="Access.Doorway"/>), so a Topic the sender may not see and one no slug names answer alike, and a visitor is told
/// to sign in whatever they sent. A form is sent on to the reply's address, a JSON request reads the Post (<c>201</c>); a
/// refusal is a problem with its word and the field a value broke, and the same id with other words is a conflict.</summary>
public sealed class ReplyController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    [HttpPost("/t/{tangent}/{topic}/{post?}")]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "reply", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Needs(Bit.Post)]
    [Description("Reply in a Topic you have read, at its address or at the address of the Post you answer. parent is the id of the Post you answer; left out, you answer the Post the address names, or else the Topic. Send an id, a TID you mint, and the same call again returns the same Post, so a retry is safe; without one, each call makes a new Post. Refused, its word says why.")]
    public async Task<IActionResult> Write([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        [FromRoute, Description(AnsweredPost)] string? post, ReplyInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        var personId = found.PersonId;
        var root = found.Root!;

        var body = await PostWrites.Body(input.Body, found.Capabilities.Holds(Bit.Post));
        var parent = input.Parent is { Length: > 0 } named ? named : found.Post?.Id;
        var outcome = await new Reply(PostWrites.Id(input.Id), root.Id, body, personId ?? "", parent).Send();
        return outcome switch
        {
            Outcome<Post>.Done { Result: var reply } => await PostWrites.Written(HttpContext, reply, root,
                PostWrites.Address(BaseAddress(), root.TangentId, root.Slug ?? root.Id, reply.Id), personId, created: true, ct),
            Outcome<Post>.Refused { Reason: var reason } => PostWrites.Refused(HttpContext, reason),
            _ => Answers.NotDone(HttpContext, outcome)
        };
    }

    /// <summary>What the Post segment of a reply's address holds.</summary>
    private const string AnsweredPost = "The Id of the Post of that Topic the reply answers, as its address names it, unless parent names another.";

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
