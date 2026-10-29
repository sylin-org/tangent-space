using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Tangents;

/// <summary>Sets up a Tangent with its audience, and appends <see cref="EventKind.TangentChanged"/> carrying that
/// audience. The place exists for the actor, who holds <see cref="Bit.View"/> on it, or the command answers that no
/// place is here; the actor holds <see cref="Bit.ManageSpace"/>, or is refused with the gate word. Idempotent by slug:
/// the same slug with the same fields and audience answers with the existing Tangent; a different one is a conflict.</summary>
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
    public const string NoPlace = "No Tangent Space is here.";

    /// <summary>Writes the Tangent's audience, its <c>everyone</c> overwrite, before the transaction that creates the
    /// Tangent opens, since Koan commits an overwrite only outside one: from the instant the Tangent's row commits, it
    /// exists for nobody its audience excludes. Only where <see cref="Run"/> goes on to create it — the place exists for
    /// the actor, who holds <see cref="Bit.ManageSpace"/>, the slug is a slug and no Tangent has it — so an existing
    /// Tangent's audience stands and an actor <see cref="Run"/> refuses writes nothing. A command that is refused or fails
    /// after this leaves the overwrite on a resource no Tangent has: every address there answers as for a missing
    /// Tangent whatever its overwrites say, and the next create of that slug writes its own audience over it.</summary>
    public async Task Prepare(CancellationToken ct)
    {
        var capabilities = await Capabilities.Load(ActorId, null, null, ct);
        if (!capabilities.Exists || capabilities.Gate(Bit.ManageSpace) is not null || !Posts.Slug.IsValid(Slug)) return;
        if (await Tangent.Get(Slug, ct) is not null) return;
        await Overwrites.SetAudience(AppHost.GetRequiredService<RoleCollection>(nameof(CreateTangent)), Slug, Audience, ct);
    }

    public async Task<Outcome<Tangent>> Run(Sequences sequences, CancellationToken ct)
    {
        var capabilities = await Capabilities.Load(ActorId, null, null, ct);
        if (!capabilities.Exists) return new Outcome<Tangent>.Refused(NoPlace);

        if (await Tangent.Get(Slug, ct) is { } existing)
        {
            var audience = await Overwrites.AudienceOf(AppHost.GetRequiredService<RoleCollection>(nameof(CreateTangent)), Slug, ct);
            return existing.Same(Name, Description, DefaultKind, Order) && audience == Audience
                ? new Outcome<Tangent>.Done(existing)
                : new Outcome<Tangent>.Conflict($"Tangent '{Slug}' already exists with different fields.");
        }

        if (capabilities.Gate(Bit.ManageSpace) is { } gate) return new Outcome<Tangent>.Refused(gate);

        var at = At ?? DateTimeOffset.UtcNow;
        var tangent = Tangent.Create(Slug, Name, Description, DefaultKind, Order);
        await tangent.Save(ct);
        await Event.Next(sequences, EventKind.TangentChanged, at, tangentId: tangent.Id, actorId: ActorId, audience: Audience).Save(ct);
        return new Outcome<Tangent>.Done(tangent);
    }
}
