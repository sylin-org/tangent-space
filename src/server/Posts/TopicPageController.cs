using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>What a move sends, as a form or one JSON object.</summary>
public sealed record MoveTopicInput([Description(Holds.MoveTo)] string? To = null);

/// <summary>What a delete of a whole Topic sends, as a form or one JSON object.</summary>
public sealed record DeleteTopicInput([Description(Holds.DeletedReplies)] int? Replies = null);

/// <summary>What following a Topic sends, as a form or one JSON object.</summary>
public sealed record FollowInput([Description(Holds.Follow)] bool? Follow = null);

/// <summary>What whoever moderates a Topic, and whoever follows one, does from its page (<see cref="TopicsController"/>), each at a
/// segment under its address that no TID is. Whoever holds <see cref="Bit.Moderate"/> on the Topic closes and reopens it at
/// <c>/t/{tangent}/{topic}/close</c> and <c>…/reopen</c>, sending <see cref="CloseTopic"/>, and pins and unpins it at <c>…/pin</c> and
/// <c>…/unpin</c>, sending <see cref="PinTopic"/>, which the Doorway judges on its root before anything is bound: a form is sent on to
/// the Topic's address, and a JSON request reads where it stands (<see cref="TopicNow"/>). Whoever holds <see cref="Bit.Moderate"/> in
/// its Tangent, a removed root's among them, moves it at <c>…/move</c>, whose <c>GET</c> is the form (<see cref="MoveTopicPage"/>) and
/// whose <c>POST</c> sends <see cref="MoveTopic"/>, and deletes it whole at <c>…/delete</c>, sending <see cref="DeleteTopic"/>; neither
/// is offered in the moderators' Tangent. A signed-in reader follows it, or stops, at <c>…/follow</c> (<see cref="Follow"/>).</summary>
public sealed class TopicPageController(KoanWebBaseUrl baseUrl, Connections.Readers readers) : ControllerBase
{
    /// <summary>The query parameter naming the sequence a window of the Topic ends before (<see cref="TopicsController"/>).</summary>
    public const string Before = "before";

    /// <summary>The query parameter naming the sequence a window of the Topic starts after.</summary>
    public const string After = "after";

    /// <summary>The most participants the page resolves: the window's authors first, then who closed it, then who removed or
    /// else edited its Posts.</summary>
    public const int PeopleLimit = 32;

    /// <summary>The segments under a Topic's address where whoever moderates there closes, reopens, pins and unpins it;
    /// each is a word no TID is.</summary>
    public const string CloseSegment = "close";
    public const string ReopenSegment = "reopen";
    public const string PinSegment = "pin";
    public const string UnpinSegment = "unpin";

    /// <summary>The segments under a Topic's address where whoever moderates its Tangent moves it and deletes it whole; each is a
    /// word no TID is.</summary>
    public const string MoveSegment = "move";
    public const string DeleteSegment = "delete";

    /// <summary>The field of the delete that says how many replies go with the Topic.</summary>
    public const string RepliesField = "replies";

    private const string Route = "/t/{tangent}/{topic}/";

    /// <summary>Where a Topic stands once a moderator's press has changed it, as a JSON answer says it: its root's id, its
    /// address, whether it is closed and whether it is pinned.</summary>
    public sealed record TopicNow(string Id, string Address, bool Closed, bool Pinned);

    /// <summary>A Topic deleted whole, as a JSON answer says it: its root's id, its title, how many replies went with it, and
    /// the address of the Tangent it was in.</summary>
    public sealed record DeletedTopic(string Id, string Title, int Replies, string Tangent);

    /// <summary>The segment under a Topic's address where a participant follows it or stops.</summary>
    public const string FollowSegment = "follow";

    /// <summary>The field that says whether to follow.</summary>
    public const string FollowField = "follow";

    /// <summary>Whether the participant follows the Topic, as a JSON answer says it: its id, its address and
    /// <c>following</c>.</summary>
    public sealed record Followed(string Topic, string Address, bool Following);

    [HttpPost(Route + CloseSegment)]
    [Tool(Name = "close_topic", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.Moderate)]
    [Description("Close a Topic to replies: every word stays and stays readable, and only its moderators reply there until reopen_topic opens it again. Closing a closed Topic changes nothing. Answers the Topic's address, closed and pinned.")]
    public async Task<IActionResult> Close([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        CancellationToken ct)
    {
        var here = Here.Of(HttpContext);
        return Moderated(await new CloseTopic(here.Root!.Id, here.PersonId ?? "").Send(), closed => (closed, here.Tangent!));
    }

    [HttpPost(Route + ReopenSegment)]
    [Tool(Name = "reopen_topic", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.Moderate)]
    [Description("Reopen a closed Topic, so everyone who may post there replies again. Reopening an open Topic changes nothing. Answers the Topic's address, closed and pinned.")]
    public async Task<IActionResult> Reopen([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        CancellationToken ct)
    {
        var here = Here.Of(HttpContext);
        return Moderated(await new CloseTopic(here.Root!.Id, here.PersonId ?? "", Close: false).Send(), opened => (opened, here.Tangent!));
    }

    [HttpPost(Route + PinSegment)]
    [Tool(Name = "pin_topic", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.Moderate)]
    [Description("Pin a Topic at the top of its Tangent: first on its first page and first on its shelf on the door. A Tangent keeps one pinned, so pinning this one unpins any other, which read_topic's pinning names. Pinning the pinned Topic changes nothing. Answers the Topic's address, closed and pinned.")]
    public async Task<IActionResult> Pin([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        CancellationToken ct)
    {
        var here = Here.Of(HttpContext);
        return Moderated(await new PinTopic(here.Root!.Id, here.PersonId ?? "").Send(), held => (here.Root!, held));
    }

    [HttpPost(Route + UnpinSegment)]
    [Tool(Name = "unpin_topic", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.Moderate)]
    [Description("Unpin a Topic, so it stands among its Tangent's Topics in their usual order again. Unpinning a Topic that is not pinned changes nothing. Answers the Topic's address, closed and pinned.")]
    public async Task<IActionResult> Unpin([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        CancellationToken ct)
    {
        var here = Here.Of(HttpContext);
        return Moderated(await new PinTopic(here.Root!.Id, here.PersonId ?? "", Pin: false).Send(), held => (here.Root!, held));
    }

    [HttpGet(Route + MoveSegment)]
    [Needs(Bit.View, Participant = true)]
    public async Task<IActionResult> MovePage([FromRoute] string tangent, [FromRoute] string topic, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var here = Here.Of(HttpContext);
        // Judged here, not declared: moving asks Moderate of the Tangent, which a removed root does not take away, where a
        // declaration asks it of the Topic.
        if (here.Around.Gate(Bit.Moderate) is { } gate) return this.Refused(gate);
        if (here.Tangent!.Id == here.Rows.Space.ModeratorsTangentId) return this.Refused(MoveTopic.KeepsReports);
        return Ok(await Moving(here, null, ct));
    }

    [HttpPost(Route + MoveSegment)]
    [Tool(Name = "move_topic", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.View, Participant = true)]
    [Description("Move a Topic, its replies with it, to another Tangent you moderate, as read_tangents names them; you moderate both. It keeps its kind, its address here forwards to its new one, and an article's copy on Bluesky follows it, or comes down where the new Tangent is private. The moderators’ Tangent keeps its topics and takes none. Answers the Topic's root at its new address.")]
    public async Task<IActionResult> Move([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        MoveTopicInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var here = Here.Of(HttpContext);
        var outcome = await new MoveTopic(here.Root!.Id, input.To ?? "", here.PersonId ?? "").Send();
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var moved }:
                return await PostWrites.Written(HttpContext, moved, moved, PostWrites.TopicAddress(BaseAddress(), moved.TangentId, moved.Slug ?? moved.Id),
                    here.PersonId, created: false, ct);
            case Outcome<Post>.Refused { Reason: { Kind: not ReasonKind.Absent } reason } when !PostWrites.WantsJson(Request):
                return new ObjectResult(await Moving(here, reason, ct)) { StatusCode = Answers.Status(HttpContext, reason) };
            case Outcome<Post>.Refused { Reason: var reason }:
                return this.Refused(reason);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    [HttpPost(Route + DeleteSegment)]
    [Tool(Name = "delete_topic", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.View, Participant = true)]
    [Description("Delete a Topic whole, for whoever moderates its Tangent: its root and every reply go for good, no tombstone stays, and an article's copy on Bluesky comes down where its author's account still lets this place. replies says how many go with it, as read_topic's replyCount counts them; a reply that landed since is refused with holds-replies. A report stays in the moderators’ Tangent. Answers the Topic's id, title, replies and Tangent.")]
    public async Task<IActionResult> Delete([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        DeleteTopicInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var here = Here.Of(HttpContext);
        var outcome = await new DeleteTopic(here.Root!.Id, here.PersonId ?? "", input.Replies ?? 0).Send();
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var deleted }:
                var tangentAddress = PostWrites.TangentAddress(BaseAddress(), deleted.TangentId);
                if (PostWrites.WantsJson(Request)) return Ok(new DeletedTopic(deleted.Id, deleted.Title ?? "", deleted.ReplyCount, tangentAddress));
                Response.Headers.Location = tangentAddress;
                return StatusCode(StatusCodes.Status303SeeOther);
            // A form sent from a page that read fewer replies than landed since reads the page again, its delete open, holding the
            // count and the warning it holds now, with why above it.
            case Outcome<Post>.Refused { Reason: { Word: DeleteTopic.HoldsRepliesWord } stale } when !PostWrites.WantsJson(Request)
                && await TopicsController.Page(HttpContext, baseUrl, readers, null, null, ct) is { Deleting: { } deleting } page:
                return new ObjectResult(page with { Deleting = deleting with { Open = true, Failure = stale.Text } }) { StatusCode = StatusCodes.Status409Conflict };
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>The form that deletes the Topic whose root is <paramref name="root"/>, at <paramref name="topicAddress"/>, with how
    /// many replies go with it, confirmed in the page with what goes: its replies, which nothing brings back, and an article's
    /// copy on Bluesky where it is <paramref name="out"/>.</summary>
    public static Form DeleteForm(string topicAddress, Post root, bool @out) => Form.Posting(topicAddress + "/" + DeleteSegment,
        new Form.Field(RepliesField, Holds.DeletedReplies, root.ReplyCount.ToString(CultureInfo.InvariantCulture), Form.Input.Hidden)) with
    {
        Tool = "delete-topic", Opens = "Delete " + PostWrites.Word(root.Kind), Submit = "Delete it for good", Busy = "Deleting…",
        Confirm = (root.ReplyCount switch
        {
            0 => "Nothing of it stays, and nothing brings it back.",
            1 => "Its one reply goes with it, and nothing brings them back.",
            _ => "Its " + root.ReplyCount.ToString(CultureInfo.InvariantCulture) + " replies go with it, and nothing brings them back."
        }) + (@out ? " Its copy on Bluesky comes down." : "")
    };

    /// <summary>The move's page for the Topic <paramref name="here"/> names, with the form whose choices are the Tangents its reader
    /// moderates but this one and the moderators', at most <see cref="Origins.DestinationLimit"/>, each as its kind and audience,
    /// and, for an article whose copy is out, that it comes down where the place or the Tangent is private; with why
    /// <paramref name="refused"/> was refused, where it was. The Tangents in order and what the reader holds on each, each one's
    /// audience by key, and an article's receipt by key: read here alone, never on the Topic page.</summary>
    private async Task<MoveTopicPage> Moving(Here here, Reason? refused, CancellationToken ct)
    {
        var (root, tangentRow, rows) = (here.Root!, here.Tangent!, here.Rows);
        var baseAddress = BaseAddress();
        var address = PostWrites.TopicAddress(baseAddress, tangentRow.Id, root.Slug ?? root.Id);
        var moderators = rows.Space.ModeratorsTangentId;
        var destinations = await ViewableTangents.Read(here.PersonId, (row, held) => held.Holds(Bit.Moderate) && row.Id != tangentRow.Id && row.Id != moderators,
            null, Origins.DestinationLimit, ct);
        var live = root.Kind == PostKind.Article && (await Connections.Receipt.Get(Connections.Receipt.Key(root.AuthorId, root.Id), ct))?.IsLive() == true;
        var choices = new List<Form.Choice>();
        foreach (var row in destinations.Tangents)
        {
            var audience = await Overwrites.AudienceOf(row.Id, ct);
            var tile = Render.Tile.For(row, baseAddress + "/");
            choices.Add(new Form.Choice(row.Id, row.Name, (row.DefaultKind == PostKind.Article ? "Articles" : "Discussions") + " · "
                + (audience == Audience.Private ? "private" : "public")
                + (live && (audience == Audience.Private || rows.Space.Audience != Audience.Public) ? " — its copy on Bluesky comes down" : ""))
            {
                Tile = (tile.Tone, tile.Initial)
            });
        }

        var form = choices.Count == 0 ? null : Form.Posting(address + "/" + MoveSegment,
            new Form.Field(MoveTopic.ToField, Holds.MoveTo, null, Form.Input.Choice)
            {
                Choices = choices, Required = true, Label = "Move it to",
                Failure = refused is { Field: MoveTopic.ToField } ? refused.Text : null
            }) with
        {
            Tool = "move-topic", Submit = "Move", Busy = "Moving…", Cancel = new Render.Onward("Back to the topic", address),
            Failure = refused is { Field: not MoveTopic.ToField } ? refused.Text : null
        };
        var back = baseUrl.PathBase + "/t/" + tangentRow.Id + "/" + (root.Slug ?? root.Id) + "/" + MoveSegment;
        return new MoveTopicPage(root.Id, root.Title ?? "", address, root.ReplyCount, form, rows.Space.Name, baseAddress + "/",
            await PostWrites.Reader(here.PersonId, here.Around, baseAddress, back, ct));
    }

    /// <summary>The form that closes the Topic at <paramref name="topicAddress"/>, or reopens it where it is
    /// <paramref name="closed"/>: one press, which the other undoes.</summary>
    public static Form ClosingForm(string topicAddress, bool closed) => Form.Posting(topicAddress + "/" + (closed ? ReopenSegment : CloseSegment)) with
    {
        Tool = closed ? "reopen-topic" : "close-topic", Submit = closed ? "Reopen" : "Close to replies", Busy = closed ? "Reopening…" : "Closing…", Quiet = true
    };

    /// <summary>The form that pins the Topic at <paramref name="topicAddress"/>, or unpins it where it is
    /// <paramref name="pinned"/>, saying first that it unpins <paramref name="unpins"/>, a link to the Topic pinned now, where
    /// one is.</summary>
    public static Form PinningForm(string topicAddress, bool pinned, string? unpins) => Form.Posting(topicAddress + "/" + (pinned ? UnpinSegment : PinSegment)) with
    {
        Tool = pinned ? "unpin-topic" : "pin-topic", Submit = pinned ? "Unpin" : "Pin", Busy = pinned ? "Unpinning…" : "Pinning…", Quiet = true,
        Note = unpins is null ? null : "Pinning this unpins " + unpins + "."
    };

    /// <summary>A moderator's press answered: a form is sent on to the Topic's address, and a JSON request reads where the
    /// Topic stands now, its root and its Tangent as <paramref name="now"/> takes them from what the command did; a refusal
    /// is its problem.</summary>
    private IActionResult Moderated<T>(Outcome<T> outcome, Func<T, (Post Root, Tangents.Tangent Tangent)> now)
    {
        Response.Headers.CacheControl = "no-store";
        if (outcome is not Outcome<T>.Done { Result: var result })
            return outcome is Outcome<T>.Refused { Reason: var reason } ? this.Refused(reason) : Answers.NotDone(HttpContext, outcome);
        var (root, tangent) = now(result);
        var address = PostWrites.TopicAddress(BaseAddress(), root.TangentId, root.Slug ?? root.Id);
        if (!PostWrites.WantsJson(Request))
        {
            Response.Headers.Location = address;
            return StatusCode(StatusCodes.Status303SeeOther);
        }

        return Ok(new TopicNow(root.Id, address, root.Closed, !root.Removed && tangent.PinnedId == root.Id));
    }

    /// <summary>Who closed the Topic whose root is <paramref name="root"/> and when, from the people the page read; null where
    /// it is open.</summary>
    public static TopicPage.Closure? Closure(Post root, IReadOnlyDictionary<string, Render.Person> people)
        => root.Closed ? new TopicPage.Closure(Person(people, root.ClosedBy ?? root.AuthorId), root.ClosedAt ?? root.CreatedAt) : null;

    [HttpPost("/t/{tangent}/{topic}/" + FollowSegment)]
    [Tool(Name = "follow_topic", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.View, Participant = true)]
    [Description("Follow a Topic, so its new replies are for you: listed in arrive's news.following and counted on your door until you read them. With follow false, stop. Starting a Topic or replying in one follows it, unless you stopped.")]
    public async Task<IActionResult> Follow([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        FollowInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var here = Here.Of(HttpContext);
        var root = here.Root!;
        var outcome = await new FollowTopic(here.PersonId!, root.Id, input.Follow ?? true).Send(ct);
        if (outcome is not Outcome<Events.Follow>.Done { Result: var row }) return Answers.NotDone(HttpContext, outcome);
        var address = PostWrites.TopicAddress(BaseAddress(), root.TangentId, root.Slug ?? root.Id);
        if (PostWrites.WantsJson(Request)) return Ok(new Followed(root.Id, address, row.On));
        Response.Headers.Location = address;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>The form that follows the Topic at <paramref name="topicAddress"/>, or, where the reader
    /// <paramref name="following"/>, stops.</summary>
    public static Render.Form FollowForm(string topicAddress, bool following)
        => Render.Form.Posting(topicAddress + "/" + FollowSegment,
                new Render.Form.Field(FollowField, Holds.Follow, following ? "false" : "true", Render.Form.Input.Hidden))
            with { Tool = "follow-topic", Submit = following ? "Unfollow" : "Follow", Quiet = true };

    public static Render.Person Person(IReadOnlyDictionary<string, Render.Person> people, string id)
        => people.TryGetValue(id, out var person) ? person : Render.Person.Left(id);

    /// <summary>The authors of these Posts, then who closed a root's Topic, then who removed or else edited any of them, at most
    /// <paramref name="limit"/>, in one keyed read, by participant Id, each with their profile's address.</summary>
    public static async Task<IReadOnlyDictionary<string, Render.Person>> People(IReadOnlyList<Post> posts, string baseAddress,
        CancellationToken ct, int limit = PeopleLimit)
    {
        var ids = posts.Select(post => post.AuthorId)
            .Concat(posts.Where(post => post.Closed).Select(post => post.ClosedBy).OfType<string>())
            .Concat(posts.Where(post => post.Removed).Select(post => post.RemovedBy).OfType<string>())
            .Concat(posts.Select(post => post.EditedBy).OfType<string>())
            .Distinct(StringComparer.Ordinal).Take(limit).ToList();
        var found = await Participant.Get(ids, ct);
        return found.OfType<Participant>().ToDictionary(row => row.Id, row => Render.Person.Of(row, baseAddress), StringComparer.Ordinal);
    }


    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
