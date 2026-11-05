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

/// <summary>What an edit sends, as a form or one JSON object.</summary>
public sealed record EditInput(
    [Description(Holds.EditVersion)] long? Version = null,
    [Description(Holds.EditBody)] string? Body = null,
    [Description(Holds.EditTitle)] string? Title = null,
    [Description(Holds.EditCover)] string? Cover = null);

/// <summary>Where a Post changes or goes: <c>/t/{tangent}/{topic}/{post}/edit</c> and <c>…/remove</c>. The edit's
/// <c>GET</c> is the form, one resource both ways (<see cref="EditPage"/>), with the removal confirmed inline; its
/// <c>POST</c> takes <see cref="EditInput"/> and sends <see cref="EditPost"/>. The removal's <c>POST</c> sends
/// <see cref="RemovePost"/>. Either is sent on to the Post's
/// address as a form, or reads the Post (<c>200</c>) as JSON; a refusal reads the form again with the reason inline,
/// or a problem with its word; an edit made from another version reads the current text — the form holding it, or
/// <see cref="StaleEdit"/>. The edit needs <see cref="Bit.EditOwn"/> and the removal <see cref="Bit.RemoveOwn"/>, which
/// <see cref="Bit.Moderate"/> implies on any Post, and the Doorway judges them before anything is bound
/// (<see cref="Access.Doorway"/>): the place's gate for every address alike, then the missing resource's answer for a Post the
/// reader may not see or that is not a Post of that Topic, then the bit.</summary>
public sealed class EditPostController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Route = "/t/{tangent}/{topic}/{post}";

    public const string EditSegment = "edit";

    public const string RemoveSegment = "remove";

    [HttpGet(Route + "/" + EditSegment)]
    [Needs(Bit.EditOwn)]
    public async Task<IActionResult> Get(string tangent, string topic, string post, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        return Ok(await Page(found.Root!, found.Post!, found.Capabilities, found.PersonId, ct));
    }

    [HttpPost(Route + "/" + EditSegment)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "edit", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.EditOwn)]
    [Description("Replace a Post's words, and a Topic's title on its root, with the version read_topic or read_post gave you. What it said before is gone from the page, and an edit of someone else's Post says who made it. An edit from another version changes nothing and answers with the Post as it stands: read that and try again.")]
    public async Task<IActionResult> Edit([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        [FromRoute, Description(Holds.Post)] string post, EditInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        var personId = found.PersonId;
        var (root, row) = (found.Root!, found.Post!);

        var body = input.Body ?? "";
        var title = PostWrites.Title(input.Title);
        if (input.Version is not (>= 0 and var version)) return await Refused(root, row, personId, input, PostWrites.VersionRule, ct);
        var may = found.Capabilities;
        var outcome = await new EditPost(row.Id, version, await PostWrites.Body(input.Body, may.Holds(Bit.EditOwn)), title,
            personId ?? "", CoverId: input.Cover).Send();
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var edited }:
                return await PostWrites.Written(HttpContext, edited, root, Address(root, edited), personId, created: false, ct);
            case Outcome<Post>.Refused { Reason: { Kind: ReasonKind.Conflict } stale }:
                return await Stale(root, row.Id, personId, body, title, stale.Text, ct);
            case Outcome<Post>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                return await Refused(root, row, personId, input, reason, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    [HttpPost(Route + "/" + RemoveSegment)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "remove", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.RemoveOwn)]
    [Description("Remove a Post: its words go, and it stays in its place as a tombstone saying who removed it. A Post already removed answers removed, and nothing changes.")]
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

    /// <summary>A refusal of an edit: for a JSON request, the problem its reason answers as; for a form, the form again
    /// with the words that were sent and the reason where it belongs.</summary>
    private async Task<IActionResult> Refused(Post root, Post row, string? personId, EditInput input, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
        return await Again(root, row.Id, personId, Answers.Status(HttpContext, reason), page =>
        {
            var sent = page with
            {
                Title = page.Root ? PostWrites.Title(input.Title) ?? page.Title : null,
                Body = input.Body ?? page.Body,
                CoverId = input.Cover is { } cover ? (cover.Length > 0 ? cover : null) : page.CoverId
            };
            return PostWrites.Concerning(reason) switch
            {
                PostWrites.About.Title => sent with { TitleFailure = reason.Text },
                PostWrites.About.Body => sent with { BodyFailure = reason.Text },
                PostWrites.About.Sent => sent with { Failure = reason.Text },
                _ => sent with { Failure = reason.Text, FailureGate = reason.Word }
            };
        }, ct);
    }

    /// <summary>An edit made from another version: the Post as it stands now, never overwritten. A JSON request reads
    /// <see cref="StaleEdit"/>; a form reads its page again holding the current text and version, with the words that
    /// were sent below them.</summary>
    private async Task<IActionResult> Stale(Post root, string postId, string? personId, string body, string? title, string reason,
        CancellationToken ct)
    {
        if (await Post.Get(postId, ct) is not { } current) return this.Refused(Capabilities.Absent);
        if (PostWrites.WantsJson(Request))
        {
            var capabilities = (await Capabilities.LoadRows(personId, current.TangentId, ct)).For(current, current.ParentId is null ? null : root);
            return new ObjectResult(new StaleEdit(StatusCodes.Status409Conflict, "Changed since you read it", reason,
                WrittenPost.Of(current, Address(root, current), capabilities.Can)))
            {
                StatusCode = StatusCodes.Status409Conflict,
                ContentTypes = { "application/problem+json" }
            };
        }

        return await Again(root, postId, personId, StatusCodes.Status409Conflict, page => page with
        {
            Failure = "This post changed after you opened it, so your words were not saved. Its current text is in the form; yours are below it.",
            SentTitle = page.Root ? title : null,
            SentBody = body
        }, ct);
    }

    /// <summary>The form again, read afresh, with the refusal applied, at this status.</summary>
    private async Task<IActionResult> Again(Post root, string postId, string? personId, int status, Func<EditPage, EditPage> refusal,
        CancellationToken ct)
    {
        if (await Post.Get(postId, ct) is not { } row) return this.Refused(Capabilities.Absent);
        var capabilities = (await Capabilities.LoadRows(personId, root.TangentId, ct)).For(row, row.ParentId is null ? null : root);
        if (!capabilities.Exists) return this.Refused(Capabilities.Absent);
        return new ObjectResult(refusal(await Page(root, row, capabilities, personId, ct))) { StatusCode = status };
    }

    private async Task<EditPage> Page(Post root, Post row, Capabilities capabilities, string? personId, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var address = Address(root, row);
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
            editable ? PostWrites.EditForm(address + "/" + EditSegment, row.Version, row.ParentId is null, bodyOptional, row.CoverId, article) : null,
            removable ? PostWrites.RemoveForm(address + "/" + RemoveSegment) : null,
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
            Author: article ? (author is null ? new Tangents.TangentPage.Author(row.AuthorId, row.AuthorId, row.AuthorId, "person", null)
                : Tangents.TangentPage.Author.Of(author)) : null,
            CreatedAt: row.CreatedAt);
    }

    private string Address(Post root, Post row) => PostWrites.Address(BaseAddress(), root.TangentId, root.Slug ?? root.Id, row.Id);

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
