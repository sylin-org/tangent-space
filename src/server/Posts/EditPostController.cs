using Koan.Data.Core.Commands;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Posts;

/// <summary>Where a Post changes or goes: <c>/t/{tangent}/{topic}/{post}/edit</c> and <c>…/remove</c>. The edit's
/// <c>GET</c> is the form, one resource both ways (<see cref="EditPage"/>), with the removal confirmed inline; its
/// <c>POST</c> takes <c>body</c>, <c>title</c> on a root and <c>version</c>, as a form or a JSON object, and sends
/// <see cref="EditPost"/>. The removal's <c>POST</c> sends <see cref="RemovePost"/>. Either is sent on to the Post's
/// address as a form, or reads the Post (<c>200</c>) as JSON; a refusal reads the form again with the reason inline,
/// or a problem with the gate; an edit made from another version reads the current text — the form holding it, or
/// <see cref="StaleEdit"/>. Each resolves the address as the Topic page does (<see cref="PostWrites.Locate"/>) before
/// anything else is judged: the place's gate for every address alike, then the missing resource's answer for a Post the
/// reader may not see or that is not a Post of that Topic.</summary>
public sealed class EditPostController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Route = "/t/{tangent}/{topic}/{post}";

    public const string EditSegment = "edit";

    public const string RemoveSegment = "remove";

    [HttpGet(Route + "/" + EditSegment)]
    public async Task<IActionResult> Get(string tangent, string topic, string post, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, topic, post, ct);
        if (found is null) return unseen!;
        return Ok(await Page(found.Root!, found.Post!, found.Capabilities, personId, ct));
    }

    [HttpPost(Route + "/" + EditSegment)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public async Task<IActionResult> Edit(string tangent, string topic, string post, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, topic, post, ct);
        if (found is null) return unseen!;
        var (root, row) = (found.Root!, found.Post!);
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var body = fields.GetValueOrDefault(PostWrites.BodyField) ?? "";
        var title = PostWrites.Title(fields);
        if (PostWrites.Version(fields) is not { } version) return await Refused(root, row, personId, fields, PostWrites.VersionRule, ct);
        var may = found.Capabilities;
        var outcome = await new EditPost(row.Id, version, await PostWrites.Body(fields, may.Holds(Bit.EditOwn) || may.Holds(Bit.Moderate)), title,
            personId ?? "", CoverId: fields.GetValueOrDefault(PostWrites.CoverField)).Send();
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var edited }:
                return await PostWrites.Written(HttpContext, edited, root, Address(root, edited), personId, created: false, ct);
            case Outcome<Post>.Refused { Reason: { Kind: ReasonKind.Conflict } stale }:
                return await Stale(root, row.Id, personId, body, title, stale.Text, ct);
            case Outcome<Post>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                return await Refused(root, row, personId, fields, reason, ct);
            default:
                return PostWrites.Answer(HttpContext, outcome);
        }
    }

    [HttpPost(Route + "/" + RemoveSegment)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public async Task<IActionResult> Remove(string tangent, string topic, string post, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, topic, post, ct);
        if (found is null) return unseen!;
        var (root, row) = (found.Root!, found.Post!);

        var outcome = await new RemovePost(row.Id, personId ?? "").Send();
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var removed }:
                return await PostWrites.Written(HttpContext, removed, root, Address(root, removed), personId, created: false, ct);
            case Outcome<Post>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                if (PostWrites.WantsJson(Request)) return PostWrites.Refused(HttpContext, reason);
                return await Again(root, row.Id, personId, PostWrites.Status(reason), page => page with { Failure = reason.Text, FailureGate = reason.Word }, ct);
            default:
                return PostWrites.Answer(HttpContext, outcome);
        }
    }

    /// <summary>A refusal of an edit: for a JSON request, the problem its reason answers as; for a form, the form again
    /// with the words that were sent and the reason where it belongs.</summary>
    private async Task<IActionResult> Refused(Post root, Post row, string? personId, IReadOnlyDictionary<string, string> fields, Reason reason,
        CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return PostWrites.Refused(HttpContext, reason);
        return await Again(root, row.Id, personId, PostWrites.Status(reason), page =>
        {
            var sent = page with
            {
                Title = page.Root ? PostWrites.Title(fields) ?? page.Title : null,
                Body = fields.GetValueOrDefault(PostWrites.BodyField) ?? page.Body,
                CoverId = fields.GetValueOrDefault(PostWrites.CoverField) is { } cover ? (cover.Length > 0 ? cover : null) : page.CoverId
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
        if (await Post.Get(postId, ct) is not { } current) return PostWrites.Absent();
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
        if (await Post.Get(postId, ct) is not { } row) return PostWrites.Absent();
        var capabilities = (await Capabilities.LoadRows(personId, root.TangentId, ct)).For(row, row.ParentId is null ? null : root);
        if (!capabilities.Exists) return PostWrites.Absent();
        return new ObjectResult(refusal(await Page(root, row, capabilities, personId, ct))) { StatusCode = status };
    }

    private async Task<EditPage> Page(Post root, Post row, Capabilities capabilities, string? personId, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var address = Address(root, row);
        var here = baseUrl.PathBase + "/t/" + root.TangentId + "/" + (root.Slug ?? root.Id) + "/" + row.Id + "/" + EditSegment;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var tangent = await Tangents.Tangent.Get(root.TangentId, ct);
        var editable = capabilities.Holds(Bit.EditOwn) || capabilities.Holds(Bit.Moderate);
        var removable = capabilities.Holds(Bit.RemoveOwn) || capabilities.Holds(Bit.Moderate);
        var token = editable || removable ? FormToken.For(HttpContext) : null;
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
            editable ? PostWrites.EditForm(address + "/" + EditSegment, row.Version, row.ParentId is null, bodyOptional, token!, row.CoverId, article) : null,
            removable ? PostWrites.RemoveForm(address + "/" + RemoveSegment, token!) : null,
            capabilities.Can,
            editable ? null : capabilities.Gate(Bit.EditOwn)?.Word,
            removable ? null : capabilities.Gate(Bit.RemoveOwn)?.Word,
            personId is null ? baseAddress + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, capabilities, baseAddress, here, ct),
            BodyOptional: bodyOptional,
            Edited: row.EditedBy is null ? null : Tangents.TangentPage.EditedOf(row, await TakeOnATangentController.People(row, ct)),
            Kind: PostWrites.Word(row.ParentId is null ? row.Kind : PostKind.Topic),
            CoverId: row.CoverId,
            Bring: article && editable ? PostWrites.MediaForm(PostWrites.TangentAddress(baseAddress, root.TangentId), token!) : null,
            Author: article ? new Tangents.TangentPage.Author(row.AuthorId, author?.Handle ?? row.AuthorId, author?.Profile.DisplayName is { Length: > 0 } name ? name : author?.Handle ?? row.AuthorId,
                author?.Kind.ToString().ToLowerInvariant() ?? "person", author?.Profile.Avatar) : null,
            CreatedAt: row.CreatedAt);
    }

    private string Address(Post root, Post row) => PostWrites.Address(BaseAddress(), root.TangentId, root.Slug ?? root.Id, row.Id);

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
