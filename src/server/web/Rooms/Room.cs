using CarpaNet.Identity;
using Koan.Data.Core.Model;
using TangentSpace.Participants;
using TangentSpace.Site;
using TangentSpace.Communities;

namespace TangentSpace.Rooms;

public sealed class Room : Entity<Room>
{
    // "home" is deliberately the stable legacy namespace. Existing room keys and
    // native Space URIs remain unchanged when a site becomes multi-Tangent.
    public string TangentKey { get; set; } = TangentCommunity.HomeKey;
    public string Title { get; set; } = "";
    public string Topic { get; set; } = "";
    public RoomAdmission Admission { get; set; }
    // A members-only channel admits the Tangent's own members without a separate room invitation.
    public bool MembersOnly { get; set; }
    public bool AllowPostEditing { get; set; }
    public bool IsLocked { get; set; }
    public string CreatorOwnerDid { get; set; } = "";
    public long PolicyRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public RoomSpaceState SpaceState { get; set; }
    public string? SpaceUri { get; set; }

    public static Room Create(TangentSite? site, string actorDid, string key, string title, RoomAdmission admission, DateTimeOffset now,
        string tangentKey = TangentCommunity.HomeKey, TangentCommunity? tangent = null, RoomSpaceState spaceState = RoomSpaceState.Pending)
    {
        RequireOwner(site, tangent, actorDid);
        return Validate(site, actorDid, key, title, admission, now, tangentKey, tangent, spaceState);
    }

    /// <summary>A tangent administrator creates a channel under delegated authority; the creator gains no ownership.</summary>
    internal static Room CreateDelegated(TangentSite? site, string actorDid, string key, string title, RoomAdmission admission,
        DateTimeOffset now, string tangentKey, TangentCommunity tangent, RoomSpaceState spaceState = RoomSpaceState.Pending)
    {
        if (site is null || !IdentityResolver.IsValidDid(actorDid))
            throw Forbidden("A delegated channel needs an established site and a verified participant.");
        if (tangent.Id != tangentKey)
            throw Invalid("The selected Tangent does not match this channel.");
        return Validate(site, actorDid, key, title, admission, now, tangentKey, tangent, spaceState);
    }

    private static Room Validate(TangentSite? site, string actorDid, string key, string title, RoomAdmission admission,
        DateTimeOffset now, string tangentKey, TangentCommunity? tangent, RoomSpaceState spaceState)
    {
        CheckKey(key);
        TangentCommunity.CheckKey(tangentKey);
        if (tangent is not null && tangent.Id != tangentKey)
            throw Invalid("The selected Tangent does not match this channel.");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > RoomConstants.MaximumTitleLength)
            throw Invalid("A room title must contain 1–120 characters.");
        if (!Enum.IsDefined(admission)) throw Invalid("Choose signed-in or invitation-only admission.");
        return new Room
        {
            Id = key, TangentKey = tangentKey, Title = title.Trim(), Admission = admission, CreatorOwnerDid = actorDid,
            PolicyRevision = 1, CreatedAt = now, UpdatedAt = now, SpaceState = spaceState
        };
    }

    public RoomPolicy CurrentPolicy(TangentSite? site, string? actorDid, RoomMembership? membership, bool suspended = false,
        TangentCommunity? tangent = null, TangentMembership? tangentMembership = null,
        ParticipantClassification classification = ParticipantClassification.Undeclared, EffectiveRestriction? restriction = null)
    {
        CheckMembership(membership, actorDid);
        CheckTangentMembership(tangent, tangentMembership, actorDid);
        var signedIn = actorDid is not null && IdentityResolver.IsValidDid(actorDid);
        var owner = signedIn && (site?.IsOwner(actorDid) == true || tangent?.IsOwner(actorDid) == true
            || (tangent is null && site is not null && (site.IsOwner(actorDid) || CreatorOwnerDid == actorDid)));
        var communityAdmitted = owner || tangent is null || tangent.CanParticipate(actorDid, tangentMembership);
        // Classification presets gate conversation participation for ordinary members; ownership is authority, not participation.
        var rights = tangent?.ParticipationRights(classification) ?? (Read: true, Write: true);
        var classificationRead = owner || rights.Read;
        var classificationWrite = owner || rights.Write;
        // A ban blocks everything; a timeout blocks writing and administration while reading stays available.
        var banned = restriction is { Banned: true } && !owner;
        var timedOut = restriction is { Banned: false } && !owner;
        var role = membership?.Role;
        var communityRole = tangentMembership?.Role;
        // A delegated community administrator manages channels; a durable room removal still overrides that authority.
        var delegatedAdmin = signedIn && communityAdmitted && !banned && communityRole == TangentRole.Admin && role != RoomRole.Removed;
        var manager = signedIn && communityAdmitted && !suspended && !banned && (role == RoomRole.Manager || delegatedAdmin || CreatorOwnerDid == actorDid && role != RoomRole.Removed);
        // Channel grants can widen a reader within an admitted Tangent. Parent admission/removal
        // remains an upper bound; a channel role cannot restore access to a private or removed Tangent.
        var roleAdmitted = role is RoomRole.Manager or RoomRole.Member or RoomRole.Reader;
        var memberRecord = communityRole is TangentRole.Member or TangentRole.Admin or TangentRole.Reader;
        var admitted = signedIn && communityAdmitted && !suspended && !banned && classificationRead && (owner || roleAdmitted || delegatedAdmin
            || MembersOnly && role is null && memberRecord
            || !MembersOnly && role is null && Admission == RoomAdmission.SignedIn && communityAdmitted);
        // Local storage is complete without a Space; Spaces storage is ready once mapped.
        var ready = SpaceState == RoomSpaceState.Local
            || SpaceState == RoomSpaceState.Ready && !string.IsNullOrWhiteSpace(SpaceUri);
        // An explicit channel grant of member or manager widens a community reader inside that channel only.
        var communityReader = !owner && communityRole == TangentRole.Reader && role is not (RoomRole.Member or RoomRole.Manager);
        var reason = site is null ? "site-unavailable" : tangent is null && TangentKey != TangentCommunity.HomeKey ? "tangent-not-found"
            : !signedIn ? "sign-in-required" : suspended ? "suspended"
            : banned ? "banned"
            : !owner && role == RoomRole.Removed ? "removed"
            : !owner && role is null && (MembersOnly ? !memberRecord : !communityAdmitted) ? "community-membership-required"
            : !classificationRead ? "participation-policy"
            : !admitted ? "invitation-required"
            : !ready ? "space-pending" : "allowed";
        return new RoomPolicy(Id, actorDid, PolicyRevision, site?.PolicyRevision ?? 0, Admission, SpaceState, SpaceUri,
            role, owner, admitted && ready,
            admitted && ready && !timedOut && !IsLocked
                && (owner || role != RoomRole.Reader && !communityReader && classificationWrite),
            (owner && !suspended) || (manager && !timedOut), owner && !suspended, reason,
            AllowPostEditing, IsLocked);
    }

    public RoomMembership ChangeMembership(TangentSite? site, string actorDid, RoomMembership? actorMembership,
        string targetDid, RoomMembership? targetMembership, RoomRole role, DateTimeOffset now,
        TangentCommunity? tangent = null, TangentMembership? tangentMembership = null)
    {
        // Callers supply the community membership that matters to this change: their own when acting on
        // delegated authority, or the target's when the owner adjusts a participant's standing. Authority
        // is never evaluated through a record belonging to someone else.
        var actorTangentMembership = tangentMembership;
        if (actorTangentMembership is not null && actorTangentMembership.ParticipantDid != actorDid)
        {
            if (actorTangentMembership.ParticipantDid != targetDid || tangent is null
                || actorTangentMembership.TangentKey != TangentKey || !Enum.IsDefined(actorTangentMembership.Role))
                throw new RoomRuleViolation(RoomDenial.MembershipMismatch, "The community membership does not belong to this channel and participant.");
            actorTangentMembership = null;
        }
        var policy = CurrentPolicy(site, actorDid, actorMembership, false, tangent, actorTangentMembership);
        CheckMembership(targetMembership, targetDid);
        if (!policy.CanManage) throw Forbidden("Only the owner or a current room manager can change room membership.");
        if (!IdentityResolver.IsValidDid(targetDid)) throw Invalid("A membership target must be an AT DID.");
        if (!Enum.IsDefined(role)) throw Invalid("Choose manager, member, reader, or removed.");
        // Ownership protections: site/tangent owners can never become membership targets. In legacy flat
        // rooms the creator is the owner; inside a Tangent the creator of a delegated channel holds no ownership.
        if (tangent?.IsOwner(targetDid) == true || (tangent is null && (site?.IsOwner(targetDid) == true || targetDid == CreatorOwnerDid)))
            throw Forbidden("Room membership cannot change or remove the owner.");
        if (!policy.CanAppointManagers && role == RoomRole.Manager)
            throw Forbidden("Only the owner can appoint room managers.");
        if (!policy.CanAppointManagers && targetMembership?.Role == RoomRole.Manager && targetDid != actorDid)
            throw Forbidden("A room manager cannot change another manager's authority.");

        Advance(now);
        return RoomMembership.Assign(this, targetDid, role, actorDid, now);
    }

    public void ChangeTopic(TangentSite? site, string actorDid, RoomMembership? membership, string topic, DateTimeOffset now,
        TangentCommunity? tangent = null, TangentMembership? tangentMembership = null)
    {
        if (!CurrentPolicy(site, actorDid, membership, false, tangent, tangentMembership).CanManage)
            throw Forbidden("Only the owner or a current room manager can change the topic.");
        if (topic is null || topic.Length > RoomConstants.MaximumTopicLength)
            throw Invalid("A topic can contain at most 2000 characters.");
        Topic = topic.Trim();
        Advance(now);
    }

    public void ChangeSettings(TangentSite? site, string actorDid, RoomMembership? membership, bool allowPostEditing,
        bool isLocked, string? title, string? topic, DateTimeOffset now, TangentCommunity? tangent = null,
        TangentMembership? tangentMembership = null)
    {
        if (!CurrentPolicy(site, actorDid, membership, false, tangent, tangentMembership).CanManage && actorDid != CreatorOwnerDid)
            throw Forbidden("Only a current Tangent or room administrator can change room settings.");
        if (title is not null && (string.IsNullOrWhiteSpace(title) || title.Trim().Length > RoomConstants.MaximumTitleLength))
            throw Invalid("A room title must contain 1–120 characters.");
        if (topic is not null && topic.Length > RoomConstants.MaximumTopicLength) throw Invalid("A topic can contain at most 2000 characters.");
        if (title is not null) Title = title.Trim();
        if (topic is not null) Topic = topic.Trim();
        AllowPostEditing = allowPostEditing; IsLocked = isLocked;
        Advance(now);
    }

    public void ChangeAdmission(TangentSite? site, string actorDid, RoomAdmission admission, DateTimeOffset now, TangentCommunity? tangent = null)
    {
        RequireOwner(site, tangent, actorDid);
        if (!Enum.IsDefined(admission)) throw Invalid("Choose signed-in or invitation-only admission.");
        Admission = admission;
        Advance(now);
    }

    public void CompleteSpace(TangentSite? site, string actorDid, long expectedRevision, string spaceUri, DateTimeOffset now,
        TangentCommunity? tangent = null, TangentMembership? tangentMembership = null)
    {
        CheckTangentMembership(tangent, tangentMembership, actorDid);
        // The site or Tangent owner maps rooms; a delegated community administrator finishes provisioning
        // for channels they manage without ever gaining ownership.
        var delegatedProvisioner = tangent is not null && IdentityResolver.IsValidDid(actorDid) && !tangent.IsOwner(actorDid)
            && tangent.CanParticipate(actorDid, tangentMembership) && (tangentMembership?.Role == TangentRole.Admin || CreatorOwnerDid == actorDid);
        if (!delegatedProvisioner) RequireOwner(site, tangent, actorDid);
        if (SpaceState == RoomSpaceState.Local)
            throw new RoomRuleViolation(RoomDenial.SpaceAlreadyMapped, "A locally stored room cannot be mapped to a Space.");
        if (SpaceState == RoomSpaceState.Ready)
        {
            if (string.Equals(SpaceUri, spaceUri, StringComparison.Ordinal)) return;
            throw new RoomRuleViolation(RoomDenial.SpaceAlreadyMapped, "A ready room cannot be mapped to another Space.");
        }
        if (expectedRevision != PolicyRevision)
            throw new RoomRuleViolation(RoomDenial.PolicyChanged, "The room policy changed; reload it before reconciling the Space mapping.");
        if (string.IsNullOrWhiteSpace(spaceUri) || !spaceUri.StartsWith("at://", StringComparison.Ordinal)
            || spaceUri.Length > 2048 || spaceUri.Any(char.IsWhiteSpace) || spaceUri.Contains('?') || spaceUri.Contains('#'))
            throw Invalid("Supply the verified canonical AT Space URI.");
        // The real Spaces integration verifies authority, type and key before calling this domain operation.
        SpaceUri = spaceUri;
        SpaceState = RoomSpaceState.Ready;
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
        if (membership is not null && (membership.RoomKey != Id || membership.ParticipantDid != participantDid
            || membership.Id != RoomMembership.Key(Id, participantDid ?? "") || !Enum.IsDefined(membership.Role)))
            throw new RoomRuleViolation(RoomDenial.MembershipMismatch, "The membership does not belong to this room and participant.");
    }

    private void CheckTangentMembership(TangentCommunity? tangent, TangentMembership? membership, string? participantDid)
    {
        if (membership is not null && (tangent is null || membership.TangentKey != TangentKey || membership.ParticipantDid != participantDid
            || membership.Id != TangentMembership.Key(TangentKey, participantDid ?? "") || !Enum.IsDefined(membership.Role)))
            throw new RoomRuleViolation(RoomDenial.MembershipMismatch, "The community membership does not belong to this channel and participant.");
    }

    private static void RequireOwner(TangentSite? site, TangentCommunity? tangent, string actorDid)
    {
        if (site is null || !IdentityResolver.IsValidDid(actorDid) || (tangent is not null ? !tangent.IsOwner(actorDid) : !site.IsOwner(actorDid)))
            throw Forbidden("Only the current Tangent owner can perform this operation.");
    }

    private void Advance(DateTimeOffset now) { PolicyRevision = checked(PolicyRevision + 1); UpdatedAt = now; }
    private static RoomRuleViolation Invalid(string reason) => new(RoomDenial.InvalidInput, reason);
    private static RoomRuleViolation Forbidden(string reason) => new(RoomDenial.Forbidden, reason);
}
