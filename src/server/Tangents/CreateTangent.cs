using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>Sets up a Tangent with its audience, and appends <see cref="EventKind.TangentChanged"/> carrying that
/// audience, whose sequence the Tangent keeps (<see cref="Tangent.LogSeq"/>). The place exists for the actor, who holds <see cref="Bit.View"/> on it, or the command answers that no
/// place is here; the actor holds <see cref="Bit.ManageSpace"/>, or is refused with the gate word. Its audience, its
/// <c>everyone</c> overwrite, is written with it, over whatever overwrite of <c>everyone</c> the slug holds. Idempotent by
/// slug: the same slug with the same fields and audience answers with the existing Tangent; a different one is a
/// conflict.</summary>
public sealed record CreateTangent(
    string Slug,
    string Name,
    string Description,
    PostKind DefaultKind,
    int Order,
    Audience Audience,
    string ActorId,
    DateTimeOffset? At = null) : ICommand<Tangent>
{
    /// <summary>The answer to an actor for whom the place does not exist.</summary>
    public static readonly Reason NoPlace = Reason.Absent("No Tangent Space is here.");

    public async Task<Outcome<Tangent>> Run(CancellationToken ct)
    {
        var capabilities = await Capabilities.Load(ActorId, null, null, ct);
        if (!capabilities.Exists) return new Outcome<Tangent>.Refused(NoPlace);

        if (await Tangent.Get(Slug, ct) is { } existing)
        {
            var audience = await Overwrites.AudienceOf(Slug, ct);
            return existing.Same(Name, Description, DefaultKind, Order) && audience == Audience
                ? new Outcome<Tangent>.Done(existing)
                : new Outcome<Tangent>.Refused(Reason.Conflict($"Tangent '{Slug}' already exists with different fields."));
        }

        if (capabilities.Gate(Bit.ManageSpace) is { } gate) return new Outcome<Tangent>.Refused(gate);

        var tangent = Tangent.Create(Slug, Name, Description, DefaultKind, Order);
        await Overwrites.SetAudience(Collection(), Slug, Audience, ct);
        tangent.LogSeq = (await Event.Append(EventKind.TangentChanged, At ?? DateTimeOffset.UtcNow, ct, tangentId: tangent.Id, actorId: ActorId, audience: Audience)).Seq;
        await tangent.Save(ct);
        return new Outcome<Tangent>.Done(tangent);
    }

    private static RoleCollection Collection() => AppHost.GetRequiredService<RoleCollection>(nameof(CreateTangent));
}
