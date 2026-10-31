using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>Renames and describes the place and sets its admission, its audience, its accent and its slots, each slot
/// sanitized and bounded as it is written (<see cref="Slots.Written"/>, <see cref="TangentSpace.Change"/>), and appends <see cref="EventKind.SpaceChanged"/>. A field left null stays as it is. The place exists for the actor, who holds
/// <see cref="Bit.View"/> on it, or the command answers that no place is here; the actor holds
/// <see cref="Bit.ManageSpace"/>, or is refused with the gate word. A command that changes nothing writes
/// nothing.</summary>
public sealed record ChangeTangentSpace(
    string ActorId,
    string? Name = null,
    string? Description = null,
    Admission? Admission = null,
    Audience? Audience = null,
    Accent? Accent = null,
    string? Head = null,
    string? Masthead = null,
    string? DoorIntro = null,
    string? ArticleHeader = null,
    string? Footer = null,
    DateTimeOffset? At = null) : ICommand<TangentSpace>
{
    /// <summary>The answer to an actor for whom the place does not exist.</summary>
    public const string NoPlace = "No Tangent Space is here.";

    public async Task<Outcome<TangentSpace>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return new Outcome<TangentSpace>.Refused(NoPlace);
        var capabilities = await Capabilities.Load(ActorId, null, null, ct);
        if (!capabilities.Exists) return new Outcome<TangentSpace>.Refused(NoPlace);
        if (capabilities.Gate(Bit.ManageSpace) is { } gate) return new Outcome<TangentSpace>.Refused(gate);

        if (!space.Change(Name ?? space.Name, Description ?? space.Description, Admission ?? space.Admission, Audience ?? space.Audience, Accent ?? space.Appearance.Accent,
                space.Slots.Written(Head, Masthead, DoorIntro, ArticleHeader, Footer)))
            return new Outcome<TangentSpace>.Done(space);
        await space.Save(ct);
        await Event.Next(sequences, EventKind.SpaceChanged, At ?? DateTimeOffset.UtcNow, actorId: ActorId).Save(ct);
        return new Outcome<TangentSpace>.Done(space);
    }
}
