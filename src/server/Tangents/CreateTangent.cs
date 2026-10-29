using Koan.Data.Core;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Tangents;

/// <summary>Sets up a Tangent with its audience, and appends <see cref="EventKind.TangentChanged"/> carrying that
/// audience. Idempotent by slug: the same slug with the same fields and audience answers with the existing Tangent; a
/// different one is a conflict.</summary>
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
    public async Task<Outcome<Tangent>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is { } existing)
        {
            var audience = (await Overwrite.Get(Overwrite.Key(Slug, Overwrite.Everyone), ct))?.AudienceStated();
            return existing.Same(Name, Description, DefaultKind, Order) && audience == Audience
                ? new Outcome<Tangent>.Done(existing)
                : new Outcome<Tangent>.Conflict($"Tangent '{Slug}' already exists with different fields.");
        }

        var at = At ?? DateTimeOffset.UtcNow;
        var tangent = Tangent.Create(Slug, Name, Description, DefaultKind, Order);
        await tangent.Save(ct);
        await Overwrite.ForAudience(tangent.Id, Audience).Save(ct);
        await Event.Next(sequences, EventKind.TangentChanged, at, tangentId: tangent.Id, actorId: ActorId, audience: Audience).Save(ct);
        return new Outcome<Tangent>.Done(tangent);
    }
}
