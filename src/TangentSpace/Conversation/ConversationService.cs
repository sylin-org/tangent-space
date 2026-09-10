using Koan.Data.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TangentSpace.AtProtocol;
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

    private static QueryDefinition Window<T>(string property, int page, int size)
    {
        var member = typeof(T).GetProperty(property)!;
        return new QueryDefinition { Page = page, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(T), [member], member.PropertyType, false, -1), false)] };
    }
}
