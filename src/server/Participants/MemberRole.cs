using Koan.Identity.Roles;
using Tangent.Access;

namespace Tangent.Participants;

/// <summary>An admitted participant holds the member role, which grants posting in an ordinary Tangent: joining a place
/// whose admission says join, and being admitted to one that says apply, both make them a member. Koan's role write is
/// not part of the writer's transaction, so whoever sends the command that admits them joins them once it commits, and
/// every sign-in joins an admitted participant who lacks the role, which repairs a write that never happened. Standing
/// outranks it, read as it stands now (<see cref="Participant.StandingAt"/>): an applicant, a declined applicant, a
/// participant timed out until a time still to come or a banned participant is never joined here, and one whose timeout
/// has ended is, as anyone admitted. One read of Koan's cached bag decides; the role is written only where it is
/// missing.</summary>
public static class MemberRole
{
    /// <summary>Joins <paramref name="participant"/> to <see cref="Roles.Member"/> where they stand admitted now and are
    /// not in it yet; true when it wrote.</summary>
    public static async Task<bool> Join(RoleCollection roles, Participant participant, CancellationToken ct)
    {
        if (participant.StandingAt(DateTimeOffset.UtcNow) != Standing.Admitted) return false;
        if ((await roles.Bag(participant.Id, authenticated: true, ct)).InRole(Roles.Member)) return false;
        await roles.Add(Roles.Member, participant.Id, ct);
        return true;
    }
}
