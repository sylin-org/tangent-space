using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;

namespace Tangent.Events;

/// <summary>What saying "seen" sends, as a form or one JSON object.</summary>
public sealed record SeenInput([Description(Holds.SeenThrough)] long? Through = null, [Description(Holds.SeenTopic)] string? Topic = null);

/// <summary>Where a participant says what they have seen, <c>POST /seen</c>, which sends <see cref="Seen"/>: with no
/// <c>topic</c>, the door's news through its <c>through</c>; with one, that Topic through the sequence its page showed.
/// It takes a form or a JSON object and answers in kind: a form is sent back to the door or to the Topic
/// (<c>303</c>), a JSON request reads the mark as it now stands. A visitor is asked to sign in before anything else is
/// said; a Topic the sender may not see and one no Id names answer alike.</summary>
public sealed class SeenController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Path = "/seen";

    /// <summary>The field that carries the sequence the reader has seen through.</summary>
    public const string ThroughField = "through";

    /// <summary>The field that carries the Topic's Id, absent for the door.</summary>
    public const string TopicField = "topic";

    private static readonly Reason SignInToSay = Reason.Gated(Capabilities.SignIn.Word!, "Saying what you have seen needs a signed-in participant.");

    /// <summary>A mark as a JSON request reads it: its scope and the last sequence seen there.</summary>
    public sealed record Answer(string Scope, long Seq);

    /// <summary>The form that says the door at <paramref name="baseAddress"/> is seen through <paramref name="through"/>,
    /// the sequence of the log it read through.</summary>
    public static Form DoorForm(string baseAddress, long through) => Form.Posting(baseAddress + Path,
        new Form.Field(ThroughField, Holds.SeenThrough, through.ToString(CultureInfo.InvariantCulture)));

    /// <summary>The form that says the Topic <paramref name="topicId"/> is seen through <paramref name="through"/>, the
    /// last sequence its page showed.</summary>
    public static Form TopicForm(string baseAddress, string topicId, long through) => Form.Posting(baseAddress + Path,
        new Form.Field(ThroughField, Holds.SeenThrough, through.ToString(CultureInfo.InvariantCulture)),
        new Form.Field(TopicField, Holds.SeenTopic, topicId));

    [HttpPost(Path)]
    [Tool(Name = "seen", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Say what you have seen, so it is no longer new to you: after arrive, its news.through, and everything up to it is seen; after read_topic, its id as topic and its through. It never moves back.")]
    public async Task<IActionResult> Say(SeenInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = User.Identity?.IsAuthenticated == true ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
        if (personId is null) return Answers.Refused(HttpContext, SignInToSay);
        var topic = string.IsNullOrEmpty(input.Topic) ? null : input.Topic;
        if (input.Through is not (>= 0 and var through))
            return NotASequence($"The {ThroughField} field carries the sequence the door or the Topic's page names as its through.");

        var outcome = await new Seen(personId, through, topic).Send();
        switch (outcome)
        {
            case Outcome<Checkpoint>.Done { Result: var mark }:
                if (PostWrites.WantsJson(Request))
                    return new ObjectResult(new Answer(mark.Scope.ToString(), mark.Seq)) { StatusCode = StatusCodes.Status200OK, ContentTypes = { "application/json" } };
                Response.Headers.Location = topic is null ? baseUrl.PathBase + "/" : await Back(topic, ct);
                return StatusCode(StatusCodes.Status303SeeOther);
            case Outcome<Checkpoint>.Refused { Reason: { Kind: ReasonKind.Invalid } reason }:
                return NotASequence(reason.Text);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>The address of the Topic whose mark moved, where a form goes back to.</summary>
    private async Task<string> Back(string topicId, CancellationToken ct)
        => await Post.Get(topicId, ct) is { } root
            ? PostWrites.TopicAddress(baseUrl.Resolve(HttpContext), root.TangentId, root.Slug ?? root.Id)
            : baseUrl.PathBase + "/";

    private static IActionResult NotASequence(string detail)
        => new Problem(StatusCodes.Status400BadRequest, detail) { Title = "Not a sequence you have seen" }.ToResult();
}
