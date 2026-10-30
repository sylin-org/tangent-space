using Koan.Data.Core;
using Koan.Core.Hosting.App;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>The moderators' own Tangent, where a report starts a Topic (<see cref="ReportPost"/>): a private Tangent —
/// its <c>everyone</c> overwrite denies <see cref="Bit.View"/> — whose <see cref="Roles.Moderator"/> overwrite allows
/// <see cref="Bit.View"/>, <see cref="Bit.Post"/> and <see cref="Bit.Topic"/>, so moderators and administrators read,
/// reply and start Topics there and nobody else finds it. First run creates it with the place
/// (<see cref="Establish"/>), and a start creates it for a place established without one
/// (<see cref="CreateModeratorsTangent"/>); the settings row records its slug. Its overwrites are written before the
/// transaction that creates it, since Koan commits an overwrite only outside one, and the transaction creates it only
/// where Koan's rows already state exactly that access, so it exists for nobody else from the instant it
/// commits.</summary>
public static class ModeratorsTangent
{
    public const string Slug = "moderators";

    public const string Name = "Moderators";

    public const string Description = "Reports, and what the moderators talk over.";

    /// <summary>Its place among the Tangents: after every other.</summary>
    public const int Order = int.MaxValue;

    /// <summary>The bits the moderator role's overwrite allows there.</summary>
    public const Bit ModeratorBits = Bit.View | Bit.Post | Bit.Topic;

    /// <summary>The answer when Koan's rows do not state the Tangent's access as the command runs, so it was not created;
    /// the next start or first run writes the access anew.</summary>
    public const string NoAccess = "The moderators' Tangent's access could not be put in place, so it was not created; try again.";

    /// <summary>The answer when every slug tried is another Tangent's.</summary>
    public const string NoSlug = "Every slug tried for the moderators' Tangent belongs to another Tangent.";

    /// <summary>The slug it takes: <see cref="Slug"/>, or the first numbered suffix of it that no Tangent holds
    /// (<see cref="Tangents.Tangent.FreeSlug"/>).</summary>
    public static Task<string?> FreeSlug(CancellationToken ct) => Tangents.Tangent.FreeSlug(Slug, ct);

    /// <summary>Writes its access on <paramref name="slug"/>'s resource, over whatever overwrites of the two subjects the
    /// resource holds: <c>everyone</c> denied <see cref="Bit.View"/>, the moderator role allowed
    /// <see cref="ModeratorBits"/>. Runs outside the writer's transaction (<see cref="ICommand{TResult}.Prepare"/>).</summary>
    public static async Task WriteAccess(string slug, CancellationToken ct)
    {
        var roles = Collection();
        await Overwrites.SetAudience(roles, slug, Audience.Private, ct);
        await roles.Overwrite(Overwrites.Resource(slug), OverwriteSubjectKind.Role, Roles.Moderator, Bits.Tokens(ModeratorBits), [], ct: ct);
    }

    /// <summary>Creates the Tangent on <paramref name="slug"/> inside the writer's transaction, with
    /// <see cref="EventKind.TangentChanged"/> carrying its private audience, or refuses with <see cref="NoAccess"/> where
    /// Koan's rows do not state its access exactly. Recording it on the settings row is the caller's.</summary>
    public static async Task<Tangents.Tangent> Create(Sequences sequences, string slug, string? actorId, DateTimeOffset at, CancellationToken ct)
    {
        var tangent = Tangents.Tangent.Create(slug, Name, Description, PostKind.Topic, Order);
        if (!await HasAccess(slug, ct)) throw new Refusal(NoAccess);
        tangent.LoggedAudience = Audience.Private;
        await tangent.Save(ct);
        await Event.Next(sequences, EventKind.TangentChanged, at, tangentId: tangent.Id, actorId: actorId, audience: Audience.Private).Save(ct);
        return tangent;
    }

    /// <summary>Whether a participant holding the moderator role and nothing more may view the Tangent
    /// <paramref name="tangentId"/>: Koan's resolution over that role's bag, <paramref name="space"/>'s audience and the
    /// Tangent's overwrites; two keyed reads of roles and the overwrites from Koan's cache. It decides what a report
    /// carries of the reported Topic.</summary>
    public static async Task<bool> MayView(string tangentId, TangentSpace space, CancellationToken ct)
    {
        var roles = Collection();
        var moderator = await roles.Get(Roles.Moderator, ct);
        var everyone = await roles.Get(Roles.Everyone, ct);
        IEnumerable<string> tokens =
            [RoleTokens.Everyone, RoleTokens.Authenticated, Roles.Moderator, .. moderator?.Permissions ?? [], .. everyone?.Permissions ?? []];
        var holder = new Participants.Participant { Id = Roles.Moderator, Standing = Participants.Standing.Admitted };
        return Capabilities.For(holder, tokens, space, await roles.Overwrites(Overwrites.Resource(tangentId), ct), DateTimeOffset.UtcNow).Exists;
    }

    /// <summary>Whether Koan's rows on <paramref name="slug"/>'s resource state its access: a private audience, and the
    /// moderator role allowed <see cref="ModeratorBits"/>.</summary>
    private static async Task<bool> HasAccess(string slug, CancellationToken ct)
    {
        var roles = Collection();
        if (await Overwrites.AudienceOf(roles, slug, ct) != Audience.Private) return false;
        var moderator = (await roles.Overwrites(Overwrites.Resource(slug), ct))
            .FirstOrDefault(row => row.SubjectKind == OverwriteSubjectKind.Role && row.SubjectId == Roles.Moderator);
        return moderator is not null && Bits.InTokens(moderator.Allow) == ModeratorBits && moderator.Deny.Count == 0;
    }

    private static RoleCollection Collection() => AppHost.GetRequiredService<RoleCollection>(nameof(ModeratorsTangent));
}
