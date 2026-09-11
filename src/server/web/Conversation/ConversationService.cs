using Koan.Data.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TangentSpace.AtProtocol;
using TangentSpace.Participants;
using TangentSpace.Rooms;

namespace TangentSpace.Conversation;

public sealed partial class ConversationService(RoomGovernance governance, SpacesService spaces,
    IOptions<SpacesOptions> options, TimeProvider clock, IDataProtectionProvider protection, ILogger<ConversationService> logger,
    SourceReadiness sourceReadiness) : IDisposable
{
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly SemaphoreSlim sync = new(1, 1);
    private readonly IDataProtector cursors = protection.CreateProtector("Tangent.Conversation.Cursor.v1");

    public Task<RoomPolicy> ReadPolicy(string did, string room, CancellationToken ct)
        => governance.WithCurrentPolicy(did, room, (policy, _) =>
        {
            if (!policy.CanRead) throw new UnauthorizedAccessException("This room's current rules do not allow reading.");
            return Task.FromResult(policy);
        }, ct);

    public void Dispose() { writes.Dispose(); sync.Dispose(); }

    /// <summary>Read-time resolution for every author and facet-referenced participant in a
    /// page (ADR 0008): fresh labels for stable identities, bounded to distinct DIDs.</summary>
    internal static async Task<IReadOnlyDictionary<string, ParticipantResolution>?> ResolveParticipants(
        IReadOnlyList<Message> messages, CancellationToken ct)
    {
        var dids = messages.Select(message => message.AuthorDid)
            .Concat(messages.Where(message => message.Facets is not null).SelectMany(message => message.Facets!)
                .Where(facet => facet.Kind == PostFacet.Mention && facet.Did is not null).Select(facet => facet.Did!))
            .Distinct(StringComparer.Ordinal).Take(32).ToList();
        if (dids.Count == 0) return null;
        var resolved = new Dictionary<string, ParticipantResolution>(StringComparer.Ordinal);
        foreach (var did in dids)
        {
            var participant = await Participant.Get(did, ct);
            if (participant is null) continue;
            resolved[did] = new ParticipantResolution(
                participant.Handle is { Length: > 0 and <= 253 } handle ? handle : null,
                null, participant.Classification.ToString());
        }
        return resolved;
    }

    private static QueryDefinition Window<T>(string property, int page, int size)
    {
        var member = typeof(T).GetProperty(property)!;
        return new QueryDefinition { Page = page, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(T), [member], member.PropertyType, false, -1), false)] };
    }
}
