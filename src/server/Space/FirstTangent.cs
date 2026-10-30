using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>The Tangent first run creates beside the moderators' (<see cref="Establish"/>), so a place can be talked in
/// from its first minute: <see cref="Name"/>, public, of discussions, with no description, after any Tangent already
/// there, as a created Tangent stands by default (<see cref="CreateTangent"/>). From then on it is an ordinary Tangent,
/// which its settings rename and change. Its audience, its <c>everyone</c> overwrite, is written before the transaction
/// that creates it, since Koan commits an overwrite only outside one, and the transaction creates it only where Koan's
/// rows already state that audience.</summary>
public static class FirstTangent
{
    public const string Slug = "general";

    public const string Name = "General";

    /// <summary>The answer when Koan's rows do not state the first Tangent's audience as first run runs, so the place was
    /// not set up; the next sign-in writes the audience anew.</summary>
    public const string NoAudience = "The first Tangent's audience could not be put in place, so the place was not set up; sign in again.";

    /// <summary>The slug it takes: <see cref="Slug"/>, or the first numbered suffix of it that no Tangent holds.</summary>
    public static Task<string?> FreeSlug(CancellationToken ct) => Tangents.Tangent.FreeSlug(Slug, ct);

    /// <summary>Writes its public audience on <paramref name="slug"/>'s resource, over whatever overwrite of
    /// <c>everyone</c> the resource holds. Runs outside the writer's transaction (<see cref="ICommand{TResult}.Prepare"/>).</summary>
    public static Task WriteAudience(string slug, CancellationToken ct) => Overwrites.SetAudience(Collection(), slug, Audience.Public, ct);

    /// <summary>Creates the Tangent on <paramref name="slug"/> inside the writer's transaction, with
    /// <see cref="EventKind.TangentChanged"/> carrying its public audience, or refuses with <see cref="NoAudience"/> where
    /// Koan's rows do not state it.</summary>
    public static async Task<Tangents.Tangent> Create(Sequences sequences, string slug, string actorId, DateTimeOffset at, CancellationToken ct)
    {
        var tangent = Tangents.Tangent.Create(slug, Name, "", PostKind.Topic, await Tangents.Tangent.NextOrder(ct));
        if (await Overwrites.AudienceOf(slug, ct) != Audience.Public) throw new Refusal(NoAudience);
        tangent.LoggedAudience = Audience.Public;
        await tangent.Save(ct);
        await Event.Next(sequences, EventKind.TangentChanged, at, tangentId: tangent.Id, actorId: actorId, audience: Audience.Public).Save(ct);
        return tangent;
    }

    private static RoleCollection Collection() => AppHost.GetRequiredService<RoleCollection>(nameof(FirstTangent));
}
