using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;

namespace Tangent.Space;

/// <summary>Renames and describes the place and sets its admission, its audience and its appearance — base, palette, the
/// place's own CSS, its image and its words in Markdown — each part judged as it is written (<see cref="Appearance.With"/>,
/// <see cref="TangentSpace.Change"/>) with the images they name in step (<see cref="MediaUse.Record(Appearance, Appearance, string, CancellationToken)"/>), and appends
/// <see cref="EventKind.SpaceChanged"/>. A field left null stays as it is. The place exists for the actor, who holds
/// <see cref="Bit.View"/> on it, or the command answers that no place is here; the actor holds
/// <see cref="Bit.ManageSpace"/>, or is refused with the gate word. A command that changes nothing writes
/// nothing.</summary>
public sealed record ChangeTangentSpace(
    string ActorId,
    string? Name = null,
    string? Description = null,
    Admission? Admission = null,
    Audience? Audience = null,
    string? Base = null,
    string? Palette = null,
    string? Css = null,
    string? Image = null,
    string? Masthead = null,
    string? Footer = null,
    string? Intro = null,
    string? ArticleHeader = null,
    DateTimeOffset? At = null) : ICommand<TangentSpace>
{
    /// <summary>The answer to an actor for whom the place does not exist.</summary>
    public static readonly Reason NoPlace = Reason.Absent("No Tangent Space is here.");

    public async Task<Outcome<TangentSpace>> Run(CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return new Outcome<TangentSpace>.Refused(NoPlace);
        var capabilities = await Capabilities.Load(ActorId, null, null, ct);
        if (!capabilities.Exists) return new Outcome<TangentSpace>.Refused(NoPlace);
        if (capabilities.Gate(Bit.ManageSpace) is { } gate) return new Outcome<TangentSpace>.Refused(gate);

        var before = space.Appearance.Copy();
        if (!space.Change(Name ?? space.Name, Description ?? space.Description, Admission ?? space.Admission, Audience ?? space.Audience,
                space.Appearance.With(Base, Palette, Css, Image, Masthead, Footer, Intro, ArticleHeader)))
            return new Outcome<TangentSpace>.Done(space);
        await space.Save(ct);
        await MediaUse.Record(before, space.Appearance, ActorId, ct);
        await Event.Append(EventKind.SpaceChanged, At ?? DateTimeOffset.UtcNow, ct, actorId: ActorId);
        return new Outcome<TangentSpace>.Done(space);
    }
}
