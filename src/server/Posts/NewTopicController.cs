using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>What starting a Topic sends, as a form or one JSON object.</summary>
public sealed record StartInput(
    [Description(Holds.TopicTitle)] string? Title = null,
    [Description(Holds.TopicBody)] string? Body = null,
    [Description(Holds.Cover)] string? Cover = null,
    [Description(Holds.Id)] string? Id = null,
    [Description(Holds.TakenFrom)] string? Origin = null);

/// <summary>Where a Topic starts: <c>/t/{tangent}/new</c>, the Tangent page's "new topic". Its <c>GET</c> is the form,
/// one resource both ways (<see cref="NewTopicPage"/>), with a TID minted into it. Its <c>POST</c> takes
/// <see cref="StartInput"/> and sends <see cref="StartTopic"/>: a form is sent on to the new Post's address, a JSON
/// request reads the Post (<c>201</c>); a refusal reads the form again with the reason inline, or a problem with its word;
/// the same id with other words is a conflict. Both need <see cref="Bit.Topic"/>, which the Doorway judges before anything is
/// bound (<see cref="Access.Doorway"/>), so a Tangent the reader may not view and one no row has answer alike. Taking a
/// Post on a tangent is this form with <c>?origin=</c> naming the Post: it quotes it, offers a title from its first
/// words, and lists the Tangents the reader may start the Topic in; an origin the reader may not view and one no row has
/// answer alike, as <see cref="StartTopic.NoOrigin"/>.</summary>
public sealed class NewTopicController(KoanWebBaseUrl baseUrl, IDataProtectionProvider protection) : ControllerBase
{
    public const string Route = "/t/{tangent}/" + TangentPageController.NewTopicSegment;

    /// <summary>The query parameter that carries the cursor of the page of Tangents to choose from.</summary>
    public const string After = "after";

    [HttpGet(Route)]
    [Needs(Bit.Topic)]
    public async Task<IActionResult> Get(string tangent, [FromQuery(Name = PostWrites.OriginField)] string? origin,
        [FromQuery(Name = After)] string? after, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        ViewableTangents.Cursor? cursor = null;
        if (after is not null && (cursor = ViewableTangents.Cursor.Open(after, protection)) is null)
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {After} parameter is the cursor a page of this form names as its next.") { Title = "Not a page of these Tangents" }.ToResult();
        return await Page(found, found.PersonId, origin, cursor, ct) is { } page ? Ok(page) : this.Refused(StartTopic.NoOrigin);
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "start_topic", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Needs(Bit.Topic)]
    [Description("Start a Topic in a Tangent, when what you have to say answers nothing already there; or take a Post on a tangent by naming it as the origin, when a reply would wander from its Topic. Send an id, a TID you mint, and the same call again returns the same Topic, so a retry is safe; without one, each call makes a new Topic. Refused, its word says why.")]
    public async Task<IActionResult> Start([FromRoute, Description(Holds.Tangent)] string tangent, StartInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        var personId = found.PersonId;

        var title = PostWrites.Title(input.Title) ?? "";
        var cover = input.Cover is { Length: > 0 } named ? named : null;
        var body = await PostWrites.Body(input.Body, found.Capabilities.Holds(Bit.Topic));
        var outcome = await new StartTopic(
            PostWrites.Id(input.Id),
            found.Tangent!.Id,
            title,
            body,
            personId ?? "",
            OriginId: input.Origin is { Length: > 0 } origin ? origin : null,
            CoverId: cover).Send();
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var root }:
                return await PostWrites.Written(HttpContext, root, root, PostWrites.Address(BaseAddress(), root.TangentId, root.Slug ?? root.Id, root.Id),
                    personId, created: true, ct);
            case Outcome<Post>.Refused { Reason: { Kind: not (ReasonKind.Absent or ReasonKind.Conflict) } reason }:
                return await Refused(tangent, personId, input, reason, ct);
            case Outcome<Post>.Refused { Reason: { Kind: ReasonKind.Conflict } reason }:
                if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
                return await Again(tangent, personId, input, StatusCodes.Status409Conflict, keepId: false, page => page with
                {
                    Failure = "This form was already sent with other words, so nothing was posted. Send it again to post these words as well."
                }, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again with the
    /// reason where it belongs.</summary>
    private async Task<IActionResult> Refused(string tangent, string? personId, StartInput input, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
        return await Again(tangent, personId, input, Answers.Status(HttpContext, reason), keepId: true, page => PostWrites.Concerning(reason) switch
        {
            PostWrites.About.Title => page with { TitleFailure = reason.Text },
            PostWrites.About.Body => page with { BodyFailure = reason.Text },
            PostWrites.About.Sent => page with { Failure = reason.Text },
            _ => page with { Gate = reason.Word, Start = null }
        }, ct);
    }

    /// <summary>The form again, read afresh, keeping the words that were sent, and the Id when it is a TID and
    /// <paramref name="keepId"/>; otherwise a new one is minted, so sending again starts a Topic of its own.</summary>
    private async Task<IActionResult> Again(string tangent, string? personId, StartInput input, int status, bool keepId,
        Func<NewTopicPage, NewTopicPage> refusal, CancellationToken ct)
    {
        var (found, refused) = await Here.Find(personId, tangent, null, null, ct);
        if (refused is not null) return this.Refused(refused);
        if (await Page(found, personId, input.Origin is { Length: > 0 } origin ? origin : null, null, ct) is not { } again)
            return this.Refused(StartTopic.NoOrigin);
        var page = again with
        {
            Title = PostWrites.Title(input.Title),
            Body = input.Body,
            CoverId = input.Cover is { Length: > 0 } cover ? cover : null
        };
        if (keepId && Tid.IsValid(input.Id)) page = page with { Start = page.Start?.With(PostWrites.IdField, input.Id) };
        return new ObjectResult(refusal(page)) { StatusCode = status };
    }

    /// <summary>The form in the Tangent <paramref name="found"/> names, taking <paramref name="origin"/> on a tangent where
    /// one is named, with a page of the Tangents to choose from after <paramref name="after"/>; null where the reader may
    /// not view the origin or no Post has its Id.</summary>
    private async Task<NewTopicPage?> Page(Here found, string? personId, string? origin, ViewableTangents.Cursor? after, CancellationToken ct)
    {
        var row = found.Tangent!;
        var capabilities = found.Capabilities;
        var baseAddress = BaseAddress();
        var address = PostWrites.TangentAddress(baseAddress, row.Id);
        var here = baseUrl.PathBase + "/t/" + row.Id + "/" + TangentPageController.NewTopicSegment;
        var space = found.Rows.Space;
        var gate = capabilities.Gate(Bit.Topic)?.Word;
        QuotedPost? quoted = null;
        if (origin is not null)
        {
            if (await Quoted(personId, origin, baseAddress, ct) is not { } taken) return null;
            quoted = taken;
            if (taken.Removed) gate ??= Capabilities.Removed.Word;
        }

        var page = new NewTopicPage(
            row.Id,
            row.Name,
            address,
            PostWrites.Word(row.DefaultKind),
            gate is null ? PostWrites.StartForm(address + "/" + TangentPageController.NewTopicSegment, row.DefaultKind, origin) : null,
            capabilities.Can,
            gate,
            personId is null ? baseAddress + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, capabilities, baseAddress, here, ct),
            Title: quoted?.OfferedTitle(),
            Bring: gate is null && row.DefaultKind == PostKind.Article ? PostWrites.MediaForm(address) : null,
            Day: DateOnly.FromDateTime(DateTime.UtcNow),
            Origin: quoted);
        if (quoted is null) return page;

        // The Tangents the reader may start it in, this one first on the first page, each a link to this form there.
        var form = "/" + TangentPageController.NewTopicSegment + "?" + PostWrites.OriginField + "=" + Uri.EscapeDataString(quoted.Id);
        var listed = await Origins.Destinations(personId, after, Origins.DestinationLimit, ct);
        var tangents = listed.Tangents.Where(other => other.Id != row.Id).ToList();
        if (after is null) tangents.Insert(0, row);
        return page with
        {
            Choices = tangents.Select(choice => new NewTopicPage.Choice(choice.Id, choice.Name, PostWrites.Word(choice.DefaultKind),
                PostWrites.TangentAddress(baseAddress, choice.Id) + form, choice.Id == row.Id)).ToList(),
            More = listed.Next is { } next ? address + form + "&" + After + "=" + Uri.EscapeDataString(next.Seal(protection)) : null,
            First = after is null ? null : address + form
        };
    }

    /// <summary>The Post <paramref name="originId"/> quoted, where the reader may view it; null where they may not or no
    /// Post has the Id. A few keyed reads: the Post, its Topic's root, its Tangent and capability's rows there.</summary>
    private static async Task<QuotedPost?> Quoted(string? personId, string originId, string baseAddress, CancellationToken ct)
    {
        if (!Tid.IsValid(originId) || await Post.Get(originId, ct) is not { } origin) return null;
        var root = origin.ParentId is null ? origin : await Post.Get(origin.TopicId, ct);
        if (root is null || await Tangents.Tangent.Get(origin.TangentId, ct) is not { } tangent) return null;
        var rows = await Capabilities.LoadRows(personId, origin.TangentId, ct);
        if (!rows.For(origin, origin.ParentId is null ? null : root).Exists) return null;
        return await QuotedPost.Of(origin, root, tangent, baseAddress, ct);
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
