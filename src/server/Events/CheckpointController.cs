using System.ComponentModel;
using System.Globalization;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;

namespace Tangent.Events;

/// <summary>What moving a checkpoint on a Topic sends, as a form or one JSON object.</summary>
public sealed record SeenInput([Description(Holds.TopicThrough)] long? Through = null);

/// <summary>What setting a watch flag sends, as a form or one JSON object.</summary>
public sealed record WatchInput([Description(Holds.Watch)] bool? Watch = null);

/// <summary>Where a participant's checkpoints on a Topic and a Tangent move, beside their pages:
/// <c>POST /t/{tangent}/{topic}/seen</c> takes <c>through</c>, the sequence the Topic page showed, and sends
/// <see cref="CatchUpTopic"/>; <c>POST /t/{tangent}/{topic}/watch</c> and <c>POST /t/{tangent}/watch</c> take
/// <c>watch</c>, <c>true</c> or <c>false</c>, and send <see cref="SetWatch"/>. Each takes a form or a JSON object and
/// answers in kind: a form is sent back to the page (<c>303</c>), a JSON request reads the checkpoint as it now stands.
/// A request a session authenticated answers to <see cref="FormToken.Validate"/> before anything else; then the address
/// is resolved as the page resolves it (<see cref="PostWrites.Locate"/>), so a Tangent or Topic the sender may not see
/// and one no address names answer alike; then a visitor is asked to sign in.</summary>
public sealed class CheckpointController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string SeenSegment = "seen";

    public const string WatchSegment = "watch";

    /// <summary>The field that carries the sequence a Topic page showed.</summary>
    public const string ThroughField = "through";

    /// <summary>The field that carries the watch flag asked for.</summary>
    public const string WatchField = "watch";

    /// <summary>A checkpoint as a JSON request reads it: its scope, the last sequence seen there (absent while nothing
    /// is), and whether the participant watches it.</summary>
    public sealed record Answer(string Scope, long? Seq, bool Watch);

    /// <summary>The form that moves the reader's checkpoint on the Topic at <paramref name="topicAddress"/> through
    /// <paramref name="through"/>, the sequence its page showed.</summary>
    public static Form SeenForm(string topicAddress, long through, FormToken token) => Form.Posting(topicAddress + "/" + SeenSegment, token,
        new Form.Field(ThroughField, Holds.TopicThrough, through.ToString(CultureInfo.InvariantCulture)));

    /// <summary>The reader's watch flag on the Tangent or Topic at <paramref name="address"/>, <paramref name="watch"/>,
    /// with the form that sets it, carrying the opposite.</summary>
    public static Watching WatchingOf(string address, bool watch, FormToken token) => new(watch, Form.Posting(address + "/" + WatchSegment, token,
        new Form.Field(WatchField, Holds.Watch + " The value given is the opposite of where the flag stands.", watch ? "false" : "true")));

    [HttpPost("/t/{tangent}/{topic}/" + SeenSegment)]
    [Tool(Name = "caught_up_in_topic")]
    [Description("Say you have seen a Topic through a sequence, so what is new there starts after it.")]
    public async Task<IActionResult> Seen([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic, SeenInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return TokenRefused();
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, topic, null, ct);
        if (found is null) return unseen!;
        if (personId is null) return PostWrites.Refused(HttpContext, Capabilities.SignIn);
        if (input.Through is not (>= 0 and var through))
            return NotASequence($"The {ThroughField} field carries the sequence the Topic page names as shown through.");

        var root = found.Root!;
        var outcome = await new CatchUpTopic(personId, root.Id, through).Send();
        return outcome switch
        {
            Outcome<Checkpoint>.Done { Result: var checkpoint } => Moved(checkpoint, PostWrites.TopicAddress(BaseAddress(), root.TangentId, root.Slug ?? root.Id)),
            Outcome<Checkpoint>.Refused { Reason: { Kind: ReasonKind.Invalid } reason } => NotASequence(reason.Text),
            _ => PostWrites.Answer(HttpContext, outcome)
        };
    }

    [HttpPost("/t/{tangent}/{topic}/" + WatchSegment)]
    [Tool(Name = "watch_topic")]
    [Description("Ask for attention on what is new in a Topic, or stop.")]
    public Task<IActionResult> WatchTopic([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic, WatchInput input, CancellationToken ct)
        => Flag(tangent, topic, input, ct);

    [HttpPost("/t/{tangent}/" + WatchSegment)]
    [Tool(Name = "watch_tangent")]
    [Description("Ask for attention on what is new in a Tangent, or stop.")]
    public Task<IActionResult> WatchTangent([FromRoute, Description(Holds.Tangent)] string tangent, WatchInput input, CancellationToken ct) => Flag(tangent, null, input, ct);

    private async Task<IActionResult> Flag(string tangent, string? topic, WatchInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return TokenRefused();
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, topic, null, ct);
        if (found is null) return unseen!;
        if (personId is null) return PostWrites.Refused(HttpContext, Capabilities.SignIn);
        if (input.Watch is not { } watch)
            return new Problem(StatusCodes.Status400BadRequest, "Not a watch flag", $"The {WatchField} field is true or false.").ToResult();

        var (scope, address) = found.Root is { } root
            ? (Scope.Topic(root.Id), PostWrites.TopicAddress(BaseAddress(), root.TangentId, root.Slug ?? root.Id))
            : (Scope.Tangent(found.Tangent.Id), PostWrites.TangentAddress(BaseAddress(), found.Tangent.Id));
        var outcome = await new SetWatch(personId, scope, watch).Send();
        return outcome is Outcome<Checkpoint>.Done { Result: var checkpoint } ? Moved(checkpoint, address) : PostWrites.Answer(HttpContext, outcome);
    }

    /// <summary>A checkpoint as it stands after the command: a form is sent back to the page at
    /// <paramref name="address"/>, a JSON request reads it.</summary>
    private IActionResult Moved(Checkpoint checkpoint, string address)
    {
        if (!PostWrites.WantsJson(Request))
        {
            Response.Headers.Location = address;
            return StatusCode(StatusCodes.Status303SeeOther);
        }

        return new ObjectResult(new Answer(checkpoint.Scope.ToString(), checkpoint.Seq == Checkpoint.Nothing ? null : checkpoint.Seq, checkpoint.Watch))
        {
            StatusCode = StatusCodes.Status200OK,
            ContentTypes = { "application/json" }
        };
    }

    private static IActionResult TokenRefused()
        => new Problem(StatusCodes.Status400BadRequest, "Open the page again",
            "This form’s token is missing or has expired, so nothing was changed.").ToResult();

    private static IActionResult NotASequence(string detail)
        => new Problem(StatusCodes.Status400BadRequest, "Not a sequence of this topic", detail).ToResult();

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
