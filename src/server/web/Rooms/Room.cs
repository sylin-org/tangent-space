using CarpaNet.Identity;
using Koan.Data.Core.Model;
using TangentSpace.Participants;
using TangentSpace.Site;
using TangentSpace.Communities;
using TangentSpace.Authorization;

namespace TangentSpace.Rooms;

public sealed class Room : Entity<Room>
{
    public string TangentKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Topic { get; set; } = "";
    public RoomAdmission Admission { get; set; }
    public RoomReadAudience ReadAudience { get; set; }
    // A members-only channel admits the Tangent's own members without a separate room invitation.
    public bool MembersOnly { get; set; }
    public bool AllowPostEditing { get; set; }
    public bool IsLocked { get; set; }
    public AccessMap Access { get; set; } = AccessMap.TopicDefaults();
    public string CreatorParticipantId { get; set; } = "";
    public long PolicyRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static Room Create(Space? space, string actorDid, string key, string title, RoomAdmission admission, DateTimeOffset now,
        Tangent tangent)
    {
        RequireOwner(space, tangent, actorDid);
        return Validate(actorDid, key, title, admission, now, tangent);
    }

    /// <summary>A tangent administrator creates a channel under delegated authority; the creator gains no ownership.</summary>
    internal static Room CreateDelegated(Space? space, string actorDid, string key, string title, RoomAdmission admission,
        DateTimeOffset now, Tangent tangent)
    {
        if (space is null || !Participant.IsValidId(actorDid))
            throw Forbidden("A delegated channel needs an established space and a verified participant.");
        return Validate(actorDid, key, title, admission, now, tangent);
    }

    private static Room Validate(string actorDid, string key, string title, RoomAdmission admission,
        DateTimeOffset now, Tangent tangent)
    {
        CheckKey(key);
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > RoomConstants.MaximumTitleLength)
            throw Invalid("A room title must contain 1–120 characters.");
        if (!Enum.IsDefined(admission)) throw Invalid("Choose signed-in or invitation-only admission.");
        return new Room
        {
            Id = key, TangentKey = tangent.Id, Title = title.Trim(), Admission = admission, CreatorParticipantId = actorDid,
            PolicyRevision = 1, CreatedAt = now, UpdatedAt = now
        };
    }

    public RoomPolicy CurrentPolicy(Space? space, string? actorDid, RoomMembership? membership, bool suspended = false,
        Tangent? tangent = null, TangentMembership? tangentMembership = null,
        ParticipantClassification classification = ParticipantClassification.Undeclared, EffectiveRestriction? restriction = null)
    {
        CheckMembership(membership, actorDid);
        CheckTangentMembership(tangent, tangentMembership, actorDid);
        var signedIn = actorDid is not null && Participant.IsValidId(actorDid);
        // A Topic whose Tangent is missing admits no one, its owners included.
        var owner = signedIn && tangent is not null && (space?.IsOwner(actorDid) == true || tangent.IsOwner(actorDid));
        var tangentAdmitted = owner || tangent?.CanParticipate(actorDid, tangentMembership) == true;
        // Classification presets gate conversation participation for ordinary members; ownership is authority, not participation.
        var rights = tangent?.ParticipationRights(classification) ?? (Read: false, Write: false);
        var classificationRead = owner || rights.Read;
        var classificationWrite = owner || rights.Write;
        // A ban blocks everything; a timeout blocks writing and administration while reading stays available.
        var banned = restriction is { Banned: true } && !owner;
        var timedOut = restriction is { Banned: false } && !owner;
        var role = membership?.Role;
        var tangentRole = tangentMembership?.Role;
        // A delegated Tangent administrator manages channels; a durable room removal still overrides that authority.
        var delegatedAdmin = signedIn && tangentAdmitted && !banned && tangentRole == TangentRole.Admin && role != RoomRole.Removed;
        var manager = signedIn && tangentAdmitted && !suspended && !banned && (role == RoomRole.Manager || delegatedAdmin || CreatorParticipantId == actorDid && role != RoomRole.Removed);
        // Channel grants can widen a reader within an admitted Tangent. Parent admission/removal
        // remains an upper bound; a channel role cannot restore access to a private or removed Tangent.
        var roleAdmitted = role is RoomRole.Manager or RoomRole.Member or RoomRole.Reader;
        var memberRecord = tangentRole is TangentRole.Member or TangentRole.Admin or TangentRole.Reader;
        var admitted = signedIn && tangentAdmitted && !suspended && !banned && classificationRead && (owner || roleAdmitted || delegatedAdmin
            || MembersOnly && role is null && memberRecord
            || !MembersOnly && role is null && Admission == RoomAdmission.SignedIn && tangentAdmitted);
        // An explicit channel grant of member or manager widens a Tangent reader inside that channel only.
        var tangentReader = !owner && tangentRole == TangentRole.Reader && role is not (RoomRole.Member or RoomRole.Manager);
        var reason = space is null ? "site-unavailable" : tangent is null ? "tangent-not-found"
            : !signedIn ? "sign-in-required" : suspended ? "suspended"
            : banned ? "banned"
            : !owner && role == RoomRole.Removed ? "removed"
            : !owner && role is null && (MembersOnly ? !memberRecord : !tangentAdmitted) ? "community-membership-required"
            : !classificationRead ? "participation-policy"
            : !admitted ? "invitation-required" : "allowed";
        return new RoomPolicy(Id, actorDid, PolicyRevision, space?.PolicyRevision ?? 0, Admission,
            role, owner, admitted,
            admitted && !timedOut && !IsLocked
                && (owner || role != RoomRole.Reader && !tangentReader && classificationWrite),
            (owner && !suspended) || (manager && !timedOut), owner && !suspended, reason,
            AllowPostEditing, IsLocked);
    }

    public RoomMembership ChangeMembership(Space? space, string actorDid, RoomMembership? actorMembership,
        string targetDid, RoomMembership? targetMembership, RoomRole role, DateTimeOffset now,
        Tangent? tangent = null, TangentMembership? tangentMembership = null, bool authorized = false)
    {
        // Callers supply the Tangent membership that matters to this change: their own when acting on
        // delegated authority, or the target's when the owner adjusts a participant's standing. Authority
        // is never evaluated through a record belonging to someone else.
        var actorTangentMembership = tangentMembership;
        if (actorTangentMembership is not null && actorTangentMembership.ParticipantId != actorDid)
        {
            if (actorTangentMembership.ParticipantId != targetDid || tangent is null
                || actorTangentMembership.TangentKey != TangentKey || !Enum.IsDefined(actorTangentMembership.Role))
                throw new RoomRuleViolation(RoomDenial.MembershipMismatch, "The Tangent membership does not belong to this channel and participant.");
            actorTangentMembership = null;
        }
        var policy = CurrentPolicy(space, actorDid, actorMembership, false, tangent, actorTangentMembership);
        CheckMembership(targetMembership, targetDid);
        if (!authorized && !policy.CanManage) throw Forbidden("Only the owner or a current room manager can change room membership.");
        if (!Participant.IsValidId(targetDid)) throw Invalid("A membership target must be a valid participant.");
        if (!Enum.IsDefined(role)) throw Invalid("Choose manager, member, reader, or removed.");
        // The Tangent owner can never become a membership target; the creator of a delegated channel holds no ownership.
        if (tangent?.IsOwner(targetDid) == true)
            throw Forbidden("Room membership cannot change or remove the owner.");
        if (!authorized && !policy.CanAppointManagers && role == RoomRole.Manager)
            throw Forbidden("Only the owner can appoint room managers.");
        if (!authorized && !policy.CanAppointManagers && targetMembership?.Role == RoomRole.Manager && targetDid != actorDid)
            throw Forbidden("A room manager cannot change another manager's authority.");

        Advance(now);
        return RoomMembership.Assign(this, targetDid, role, actorDid, now);
    }

    public void ChangeTopic(Space? space, string actorDid, RoomMembership? membership, string topic, DateTimeOffset now,
        Tangent? tangent = null, TangentMembership? tangentMembership = null, bool authorized = false)
    {
        if (!authorized && !CurrentPolicy(space, actorDid, membership, false, tangent, tangentMembership).CanManage)
            throw Forbidden("Only the owner or a current room manager can change the topic.");
        if (topic is null || topic.Length > RoomConstants.MaximumTopicLength)
            throw Invalid("A topic can contain at most 2000 characters.");
        Topic = topic.Trim();
        Advance(now);
    }

    public void ChangeSettings(Space? space, string actorDid, RoomMembership? membership, bool allowPostEditing,
        bool isLocked, string? title, string? topic, DateTimeOffset now, Tangent? tangent = null,
        TangentMembership? tangentMembership = null, bool authorized = false)
    {
        // Creation history is not a fallback grant: removals and parent admission
        // changes must revoke settings access just as they revoke other management.
        if (!authorized && !CurrentPolicy(space, actorDid, membership, false, tangent, tangentMembership).CanManage)
            throw Forbidden("Only a current Tangent or room administrator can change room settings.");
        if (title is not null && (string.IsNullOrWhiteSpace(title) || title.Trim().Length > RoomConstants.MaximumTitleLength))
            throw Invalid("A room title must contain 1–120 characters.");
        if (topic is not null && topic.Length > RoomConstants.MaximumTopicLength) throw Invalid("A topic can contain at most 2000 characters.");
        if (title is not null) Title = title.Trim();
        if (topic is not null) Topic = topic.Trim();
        AllowPostEditing = allowPostEditing; IsLocked = isLocked;
        Advance(now);
    }

    public void ChangeAccess(Space? space, string actorDid, RoomMembership? membership, AccessMap access,
        DateTimeOffset now, Tangent? tangent = null, TangentMembership? tangentMembership = null, bool authorized = false)
    {
        if (!authorized && !CurrentPolicy(space, actorDid, membership, false, tangent, tangentMembership).CanManage)
            throw Forbidden("Only a current Tangent or Topic administrator can change Topic access.");
        try { Access = (access ?? throw new ArgumentNullException(nameof(access))).NormalizeForTopic(); }
        catch (ArgumentException invalid) { throw Invalid(invalid.Message); }
        Advance(now);
    }

    public void ChangeAdmission(Space? space, string actorDid, RoomAdmission admission, DateTimeOffset now,
        Tangent? tangent = null, bool authorized = false)
    {
        if (!authorized) RequireOwner(space, tangent, actorDid);
        if (!Enum.IsDefined(admission)) throw Invalid("Choose signed-in or invitation-only admission.");
        Admission = admission;
        Advance(now);
    }

    public void ChangeReadAudience(Space? space, string actorDid, RoomReadAudience audience,
        bool publishExistingHistory, DateTimeOffset now, Tangent? tangent = null, bool authorized = false)
    {
        if (!authorized && (space is null || !Participant.IsValidId(actorDid)
            || !space.IsOwner(actorDid) && tangent?.IsOwner(actorDid) != true))
            throw Forbidden("Only the current Host or Tangent owner can publish a Topic.");
        if (!Enum.IsDefined(audience)) throw Invalid("Choose restricted or public reading.");
        if (ReadAudience != RoomReadAudience.Public && audience == RoomReadAudience.Public && !publishExistingHistory)
            throw Invalid("Confirm that the Topic's existing history will become publicly readable.");
        if (ReadAudience == audience) return;
        ReadAudience = audience;
        Advance(now);
    }

    internal static void CheckKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > RoomConstants.MaximumKeyLength
            || key[0] == '-' || key[^1] == '-' || key.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-'))
            throw Invalid("A room key must contain 1–64 lowercase letters, digits or interior hyphens.");
    }

    private void CheckMembership(RoomMembership? membership, string? participantDid)
    {
        if (membership is not null && (membership.RoomKey != Id || membership.ParticipantId != participantDid
            || membership.Id != RoomMembership.Key(Id, participantDid ?? "") || !Enum.IsDefined(membership.Role)))
            throw new RoomRuleViolation(RoomDenial.MembershipMismatch, "The membership does not belong to this room and participant.");
    }

    private void CheckTangentMembership(Tangent? tangent, TangentMembership? membership, string? participantDid)
    {
        if (membership is not null && (tangent is null || membership.TangentKey != TangentKey || membership.ParticipantId != participantDid
            || membership.Id != TangentMembership.Key(TangentKey, participantDid ?? "") || !Enum.IsDefined(membership.Role)))
            throw new RoomRuleViolation(RoomDenial.MembershipMismatch, "The Tangent membership does not belong to this channel and participant.");
    }

    private static void RequireOwner(Space? space, Tangent? tangent, string actorDid)
    {
        if (space is null || !Participant.IsValidId(actorDid) || tangent?.IsOwner(actorDid) != true)
            throw Forbidden("Only the current Tangent owner can perform this operation.");
    }

    private void Advance(DateTimeOffset now) { PolicyRevision = checked(PolicyRevision + 1); UpdatedAt = now; }
    private static RoomRuleViolation Invalid(string reason) => new(RoomDenial.InvalidInput, reason);
    private static RoomRuleViolation Forbidden(string reason) => new(RoomDenial.Forbidden, reason);
}
