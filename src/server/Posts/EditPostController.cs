using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Posts;

/// <summary>Where a Post's edit is offered, and where it goes or comes back: <c>/t/{tangent}/{topic}/{post}/edit</c>, the form that
/// edits it (<see cref="PostsController"/>'s <c>PATCH</c>), one resource both ways (<see cref="EditPage"/>), with the removal confirmed
/// inline; <c>…/remove</c>, whose <c>POST</c> sends <see cref="RemovePost"/>; and <c>…/undo-removal</c>, whose <c>POST</c>, from a
/// tombstone's footer, sends <see cref="Posts.UndoRemoval"/>, which judges who may and answers a refusal as its problem. Each write is
/// sent on to the Post's address as a form, or reads the Post (<c>200</c>) as JSON; a refused removal reads the form again with the
/// reason inline, or a problem with its word. The form needs <see cref="Bit.EditOwn"/> and the removal <see cref="Bit.RemoveOwn"/>,
/// which <see cref="Bit.Moderate"/> implies on any Post, and the Doorway judges them before anything is bound
/// (<see cref="Access.Doorway"/>): the place's gate for every address alike, then the missing resource's answer for a Post the reader
/// may not see or that is not a Post of that Topic, then the bit.</summary>
public sealed class EditPostController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Route = "/t/{tangent}/{topic}/{post}";

    public const string EditSegment = "edit";

    public const string RemoveSegment = "remove";

    public const string UndoSegment = "undo-removal";

    [HttpGet(Route + "/" + EditSegment)]
    [Needs(Bit.EditOwn)]
    public async Task<IActionResult> Get(string tangent, string topic, string post, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        return Ok(await Page(HttpContext, baseUrl, found.Root!, found.Post!, found.Capabilities, found.PersonId, ct));
    }

    [HttpPost(Route + "/" + RemoveSegment)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "remove", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.RemoveOwn)]
    [Description("Remove a Post: its words leave the page, kept so the removal can be undone, and it stays in its place as a tombstone saying who removed it. Removing a Topic's root closes the Topic: its replies stay, and it takes no more. A Post already removed answers removed, and nothing changes.")]
    public async Task<IActionResult> Remove([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        [FromRoute, Description(Holds.Post)] string post, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        var personId = found.PersonId;
        var (root, row) = (found.Root!, found.Post!);

        var outcome = await new RemovePost(row.Id, personId ?? "").Send();
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var removed }:
                return await PostWrites.Written(HttpContext, removed, root, Address(root, removed), personId, created: false, ct);
            case Outcome<Post>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
                return await Again(root, row.Id, personId, Answers.Status(HttpContext, reason), page => page with { Failure = reason.Text, FailureGate = reason.Word }, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    [HttpPost(Route + "/" + UndoSegment)]
    [Tool(Name = "undo_removal", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.View, Participant = true)]
    [Description("Undo a Post's removal: its words come back as they stood, and a Topic whose opening it is takes replies again. Whoever removed their own Post undoes that, and only they; a removal by someone else, a moderator of its Tangent undoes. A Post that stands answers as it is.")]
    public async Task<IActionResult> UndoRemoval([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        [FromRoute, Description(Holds.Post)] string post, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        var (root, row) = (found.Root!, found.Post!);
        var outcome = await new Posts.UndoRemoval(row.Id, found.PersonId ?? "").Send();
        return outcome is Outcome<Post>.Done { Result: var back }
            ? await PostWrites.Written(HttpContext, back, root, Address(root, back), found.PersonId, created: false, ct)
            : Answers.NotDone(HttpContext, outcome);
    }

    /// <summary>The form again, read afresh, with the refusal applied, at this status.</summary>
    private Task<IActionResult> Again(Post root, string postId, string? personId, int status, Func<EditPage, EditPage> refusal, CancellationToken ct)
        => Again(HttpContext, baseUrl, root, postId, personId, status, refusal, ct);

    /// <summary>The form that edits <paramref name="postId"/> again, read afresh as <paramref name="personId"/> may edit it, with
    /// <paramref name="refusal"/> applied, at this status; the missing resource's answer where they may no longer see it.</summary>
    public static async Task<IActionResult> Again(HttpContext http, KoanWebBaseUrl baseUrl, Post root, string postId, string? personId, int status,
        Func<EditPage, EditPage> refusal, CancellationToken ct)
    {
        if (await Post.Get(postId, ct) is not { } row) return Answers.Refused(http, Capabilities.Absent);
        var capabilities = (await Capabilities.LoadRows(personId, root.TangentId, ct)).For(row, row.ParentId is null ? null : root);
        if (!capabilities.Exists) return Answers.Refused(http, Capabilities.Absent);
        return new ObjectResult(refusal(await Page(http, baseUrl, root, row, capabilities, personId, ct))) { StatusCode = status };
    }

    private static async Task<EditPage> Page(HttpContext http, KoanWebBaseUrl baseUrl, Post root, Post row, Capabilities capabilities, string? personId,
        CancellationToken ct)
    {
        var baseAddress = baseUrl.Resolve(http);
        var address = PostWrites.Address(baseAddress, root.TangentId, root.Slug ?? root.Id, row.Id);
        var here = baseUrl.PathBase + "/t/" + root.TangentId + "/" + (root.Slug ?? root.Id) + "/" + row.Id + "/" + EditSegment;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var tangent = await Tangents.Tangent.Get(root.TangentId, ct);
        var editable = capabilities.Holds(Bit.EditOwn);
        var removable = capabilities.Holds(Bit.RemoveOwn);
        var bodyOptional = row.ParentId is null && row.OriginId is not null;
        var article = row.ParentId is null && row.Kind == PostKind.Article;
        var author = await Participants.Participant.Get(row.AuthorId, ct);
        return new EditPage(
            row.Id,
            address,
            root.TangentId,
            tangent?.Name ?? root.TangentId,
            PostWrites.TangentAddress(baseAddress, root.TangentId),
            root.Title ?? "",
            PostWrites.TopicAddress(baseAddress, root.TangentId, root.Slug ?? root.Id),
            row.ParentId is null,
            row.ParentId is null ? row.Title : null,
            row.Body,
            row.Version,
            row.Removed,
            editable ? PostWrites.EditForm(address, row.Version, row.ParentId is null, bodyOptional, row.CoverId, article) : null,
            removable ? PostWrites.RemoveForm(address + "/" + RemoveSegment, row,
                article ? "Remove this article" : row.ParentId is null ? "Remove this topic’s opening post" : "Remove this post") : null,
            capabilities.Can,
            editable ? null : capabilities.Gate(Bit.EditOwn)?.Word,
            removable ? null : capabilities.Gate(Bit.RemoveOwn)?.Word,
            personId is null ? baseAddress + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, capabilities, baseAddress, here, ct),
            BodyOptional: bodyOptional,
            Edited: row.EditedBy is null ? null : Tangents.TangentPage.EditedOf(row, await QuotedPost.People(row, ct)),
            Kind: PostWrites.Word(row.ParentId is null ? row.Kind : PostKind.Topic),
            CoverId: row.CoverId,
            Bring: editable ? PostWrites.MediaForm(PostWrites.TangentAddress(baseAddress, root.TangentId)) : null,
            Author: article ? (author is null ? Render.Person.Left(row.AuthorId) : Render.Person.Of(author)) : null,
            CreatedAt: row.CreatedAt);
    }

    private string Address(Post root, Post row) => PostWrites.Address(BaseAddress(), root.TangentId, root.Slug ?? root.Id, row.Id);

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
