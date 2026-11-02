using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Tangents;

namespace Tangent.Space;

/// <summary>The Tangent first run creates beside the moderators' (<see cref="Establish"/>), so a place can be talked in
/// from its first minute: <see cref="Name"/>, public, of discussions, with no description, after any Tangent already
/// there, as a created Tangent stands by default (<see cref="CreateTangent"/>). From then on it is an ordinary Tangent,
/// which its settings rename and change.</summary>
public static class FirstTangent
{
    public const string Slug = "general";

    public const string Name = "General";

    /// <summary>The slug it takes: <see cref="Slug"/>, or the first numbered suffix of it that no Tangent holds.</summary>
    public static Task<string?> FreeSlug(CancellationToken ct) => Tangents.Tangent.FreeSlug(Slug, ct);

    /// <summary>Creates the Tangent on <paramref name="slug"/> inside the running command, with its public audience, its
    /// <c>everyone</c> overwrite, over whatever overwrite of <c>everyone</c> the resource holds, and
    /// <see cref="EventKind.TangentChanged"/> carrying it.</summary>
    public static async Task<Tangents.Tangent> Create(string slug, string actorId, DateTimeOffset at, CancellationToken ct)
    {
        var tangent = Tangents.Tangent.Create(slug, Name, "", PostKind.Topic, await Tangents.Tangent.NextOrder(ct));
        await Overwrites.SetAudience(Collection(), slug, Audience.Public, ct);
        await tangent.Save(ct);
        await Event.Append(EventKind.TangentChanged, at, ct, tangentId: tangent.Id, actorId: actorId, audience: Audience.Public);
        return tangent;
    }

    private static RoleCollection Collection() => AppHost.GetRequiredService<RoleCollection>(nameof(FirstTangent));
}
