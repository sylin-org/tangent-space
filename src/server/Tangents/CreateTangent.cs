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
/// place is here; the actor holds <see cref="Bit.ManageSpace"/>, or is refused with the gate word. The Tangent is
/// created only where Koan's rows state exactly the audience asked for. Idempotent by slug: the same slug with the same
/// fields and audience answers with the existing Tangent; a different one is a conflict.</summary>
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

    /// <summary>The answer when the audience the command asks for is not what Koan's rows state as it runs, so the
    /// Tangent could not exist for exactly its audience; sending the command again writes the audience anew.</summary>
    public const string NoAudience = "The Tangent's audience could not be put in place, so the Tangent was not created; send it again.";

    /// <summary>The answer at the bound on the number of Tangents: Koan's bound on the overwrites one subject holds,
    /// since each Tangent holds one <c>everyone</c> overwrite, named by its <paramref name="setting"/>.</summary>
    public static string AtBound(string setting) =>
        $"This Tangent Space holds as many Tangents as {setting} allows; raise that setting to create another.";

    /// <summary>Writes the Tangent's audience, its <c>everyone</c> overwrite, over whatever overwrite of <c>everyone</c>
    /// the slug holds, before the transaction that creates the Tangent opens, since Koan commits an overwrite only outside
    /// one: from the instant the Tangent's row commits, it exists for nobody its audience excludes. Only where
    /// <see cref="Run"/> goes on to create it — the place exists for the actor, who holds <see cref="Bit.ManageSpace"/>,
    /// the slug is a slug, the name and description are a Tangent's and no Tangent has the slug — so an existing
    /// Tangent's audience stands and a command <see cref="Run"/> refuses writes nothing. At Koan's bound on overwrites the command is refused with the setting to raise, and
    /// writes nothing. A command that is refused or fails after this leaves the overwrite on a resource no Tangent has:
    /// every address there answers as for a missing Tangent whatever its overwrites say, and the next create of that
    /// slug writes its own audience over it.</summary>
    public async Task Prepare(CancellationToken ct)
    {
        var capabilities = await Capabilities.Load(ActorId, null, null, ct);
        if (!capabilities.Exists || capabilities.Gate(Bit.ManageSpace) is not null || !Posts.Slug.IsValid(Slug)) return;
        if (await Tangent.Get(Slug, ct) is not null) return;
        try { Tangent.Create(Slug, Name, Description, DefaultKind, Order); }
        catch (Refusal) { return; }
        try
        {
            await Overwrites.SetAudience(Collection(), Slug, Audience, ct);
        }
        catch (RoleBoundException bound)
        {
            throw new Refusal(AtBound(bound.ConfigurationKey));
        }
    }

    /// <summary>Creates the Tangent where the actor may, and only where Koan's rows state exactly the audience asked
    /// for, read after the actor's own access: roles change outside the writer, so the step before this one may have
    /// left the audience unwritten, and an overwrite a failed create left behind is never adopted.</summary>
    public async Task<Outcome<Tangent>> Run(Sequences sequences, CancellationToken ct)
    {
        var capabilities = await Capabilities.Load(ActorId, null, null, ct);
        if (!capabilities.Exists) return new Outcome<Tangent>.Refused(NoPlace);

        if (await Tangent.Get(Slug, ct) is { } existing)
        {
            var audience = await Overwrites.AudienceOf(Collection(), Slug, ct);
            return existing.Same(Name, Description, DefaultKind, Order) && audience == Audience
                ? new Outcome<Tangent>.Done(existing)
                : new Outcome<Tangent>.Conflict($"Tangent '{Slug}' already exists with different fields.");
        }

        if (capabilities.Gate(Bit.ManageSpace) is { } gate) return new Outcome<Tangent>.Refused(gate);

        var tangent = Tangent.Create(Slug, Name, Description, DefaultKind, Order);
        if (await Overwrites.AudienceOf(Collection(), Slug, ct) != Audience) return new Outcome<Tangent>.Refused(NoAudience);

        var at = At ?? DateTimeOffset.UtcNow;
        await tangent.Save(ct);
        await Event.Next(sequences, EventKind.TangentChanged, at, tangentId: tangent.Id, actorId: ActorId, audience: Audience).Save(ct);
        return new Outcome<Tangent>.Done(tangent);
    }

    private static RoleCollection Collection() => AppHost.GetRequiredService<RoleCollection>(nameof(CreateTangent));
}
