using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Tangents;

namespace Tangent.Space;

/// <summary>The moderators' own Tangent, where a report starts a Topic (<see cref="ReportPost"/>): a private Tangent —
/// its <c>everyone</c> overwrite denies <see cref="Bit.View"/> — whose <see cref="Roles.Moderator"/> overwrite allows
/// <see cref="Bit.View"/>, <see cref="Bit.Post"/> and <see cref="Bit.Topic"/>, so moderators and administrators read,
/// reply and start Topics there and nobody else finds it. First run creates it with the place
/// (<see cref="Establish"/>), and a start creates it for a place established without one
/// (<see cref="CreateModeratorsTangent"/>); the settings row records its slug. Its overwrites commit with it, so it exists
/// for nobody else from the instant it commits.</summary>
public static class ModeratorsTangent
{
    public const string Slug = "moderators";

    public const string Name = "Moderators";

    public const string Description = "Reports, and what the moderators talk over.";

    /// <summary>Its place among the Tangents: after every other.</summary>
    public const int Order = int.MaxValue;

    /// <summary>The bits the moderator role's overwrite allows there.</summary>
    public const Bit ModeratorBits = Bit.View | Bit.Post | Bit.Topic;

    /// <summary>The answer when every slug tried is another Tangent's.</summary>
    public static readonly Reason NoSlug = Reason.Invalid("Every slug tried for the moderators' Tangent belongs to another Tangent.");

    /// <summary>The slug it takes: <see cref="Slug"/>, or the first numbered suffix of it that no Tangent holds
    /// (<see cref="Tangents.Tangent.FreeSlug"/>).</summary>
    public static Task<string?> FreeSlug(CancellationToken ct) => Tangents.Tangent.FreeSlug(Slug, ct);

    /// <summary>Creates the Tangent on <paramref name="slug"/> inside the running command, with its access over whatever
    /// overwrites of the two subjects the resource holds: <c>everyone</c> denied <see cref="Bit.View"/>, the moderator role
    /// allowed <see cref="ModeratorBits"/>; and <see cref="EventKind.TangentChanged"/> carrying its private audience.
    /// Recording it on the settings row is the caller's.</summary>
    public static async Task<Tangents.Tangent> Create(string slug, string? actorId, DateTimeOffset at, CancellationToken ct)
    {
        var tangent = Tangents.Tangent.Create(slug, Name, Description, PostKind.Topic, Order);
        var roles = Collection();
        await Overwrites.SetAudience(roles, slug, Audience.Private, ct);
        await roles.Overwrite(Overwrites.Resource(slug), OverwriteSubjectKind.Role, Roles.Moderator, Bits.Tokens(ModeratorBits), [], ct: ct);
        await tangent.Save(ct);
        await Event.Append(EventKind.TangentChanged, at, ct, tangentId: tangent.Id, actorId: actorId, audience: Audience.Private);
        return tangent;
    }

    /// <summary>Whether a participant holding the moderator role and nothing more may view the Tangent
    /// <paramref name="tangentId"/>: Koan's resolution over that role's bag, <paramref name="space"/>'s audience and the
    /// overwrites that bag meets on the Tangent's resource; two keyed reads of roles and the overwrites from Koan's cache.
    /// It decides what a report carries of the reported Topic.</summary>
    public static async Task<bool> MayView(string tangentId, TangentSpace space, CancellationToken ct)
    {
        var roles = Collection();
        var moderator = await roles.Get(Roles.Moderator, ct);
        var everyone = await roles.Get(Roles.Everyone, ct);
        IEnumerable<string> permissions = [.. moderator?.Permissions ?? [], .. everyone?.Permissions ?? []];
        var holder = new Participants.Participant { Id = Roles.Moderator, Standing = Participants.Standing.Admitted };
        var bag = new RoleBag(holder.Id, authenticated: true, [Roles.Moderator],
            permissions.Where(permission => permission.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal)));
        var overwrites = await roles.OverwritesFor(bag, Overwrites.Resource(tangentId), ct);
        return Capabilities.For(holder, bag.Tokens, space, tangentId, overwrites, DateTimeOffset.UtcNow).Exists;
    }

    private static RoleCollection Collection() => AppHost.GetRequiredService<RoleCollection>(nameof(ModeratorsTangent));
}
