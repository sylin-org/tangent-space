using System.Globalization;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Writer;

namespace Tangent.Events;

/// <summary>Where a participant's checkpoints on a Topic and a Tangent move, beside their pages:
/// <c>POST /t/{tangent}/{topic}/seen</c> takes <c>through</c>, the sequence the Topic page showed, and sends
/// <see cref="CatchUpTopic"/>; <c>POST /t/{tangent}/{topic}/watch</c> and <c>POST /t/{tangent}/watch</c> take
/// <c>watch</c>, <c>true</c> or <c>false</c>, and send <see cref="SetWatch"/>. Each takes a form or a JSON object and
/// answers in kind: a form is sent back to the page (<c>303</c>), a JSON request reads the checkpoint as it now stands.
/// A request a session authenticated answers to <see cref="FormToken.Validate"/> before anything else; then the address
/// is resolved as the page resolves it (<see cref="PostWrites.Locate"/>), so a Tangent or Topic the sender may not see
/// and one no address names answer alike; then a visitor is asked to sign in.</summary>
public sealed class CheckpointController(KoanWebBaseUrl baseUrl, WriteLoop writer) : ControllerBase
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

    [HttpPost("/t/{tangent}/{topic}/" + SeenSegment)]
    public async Task<IActionResult> Seen(string tangent, string topic, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return TokenRefused();
        var (found, unseen) = await PostWrites.Locate(personId, tangent, topic, null, ct);
        if (found is null) return unseen!;
        if (personId is null) return PostWrites.Gated(Capabilities.SignIn);
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();
        if (!long.TryParse(fields.GetValueOrDefault(ThroughField), NumberStyles.None, CultureInfo.InvariantCulture, out var through))
            return NotASequence($"The {ThroughField} field carries the sequence the Topic page names as shown through.");

        var root = found.Root!;
        return await writer.Send(new CatchUpTopic(personId, root.Id, through), ct) switch
        {
            Outcome<Checkpoint>.Done { Result: var checkpoint } => Moved(checkpoint, PostWrites.TopicAddress(Configured(), root.TangentId, root.Slug ?? root.Id)),
            Outcome<Checkpoint>.Refused { Reason: CatchUpTopic.NoTopic } => PostWrites.Absent(),
            Outcome<Checkpoint>.Refused { Reason: var reason } => NotASequence(reason),
            Outcome<Checkpoint>.Failed { Cause: var cause } => throw new InvalidOperationException($"Marking {tangent}/{topic} seen failed.", cause),
            var other => throw new InvalidOperationException($"The writer answered {other} for marking {tangent}/{topic} seen.")
        };
    }

    [HttpPost("/t/{tangent}/{topic}/" + WatchSegment)]
    public Task<IActionResult> WatchTopic(string tangent, string topic, CancellationToken ct) => Flag(tangent, topic, ct);

    [HttpPost("/t/{tangent}/" + WatchSegment)]
    public Task<IActionResult> WatchTangent(string tangent, CancellationToken ct) => Flag(tangent, null, ct);

    private async Task<IActionResult> Flag(string tangent, string? topic, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return TokenRefused();
        var (found, unseen) = await PostWrites.Locate(personId, tangent, topic, null, ct);
        if (found is null) return unseen!;
        if (personId is null) return PostWrites.Gated(Capabilities.SignIn);
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();
        if (!bool.TryParse(fields.GetValueOrDefault(WatchField), out var watch))
            return new Problem(StatusCodes.Status400BadRequest, "Not a watch flag", $"The {WatchField} field is true or false.").ToResult();

        var (scope, address) = found.Root is { } root
            ? (Scope.Topic(root.Id), PostWrites.TopicAddress(Configured(), root.TangentId, root.Slug ?? root.Id))
            : (Scope.Tangent(found.Tangent.Id), PostWrites.TangentAddress(Configured(), found.Tangent.Id));
        return await writer.Send(new SetWatch(personId, scope, watch), ct) switch
        {
            Outcome<Checkpoint>.Done { Result: var checkpoint } => Moved(checkpoint, address),
            Outcome<Checkpoint>.Refused => PostWrites.Absent(),
            Outcome<Checkpoint>.Failed { Cause: var cause } => throw new InvalidOperationException($"Setting the watch on {scope} failed.", cause),
            var other => throw new InvalidOperationException($"The writer answered {other} for setting the watch on {scope}.")
        };
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

    private string Configured() => baseUrl.Configured
        ?? throw new InvalidOperationException("Koan:Web:ApplicationUrl must be set to the base URL the Tangent Space answers at.");
}
