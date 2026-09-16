using CarpaNet.Identity;
using Koan.Data.Core.Model;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Site;
using TangentSpace.Authorization;

namespace TangentSpace.Communities;

/// <summary>A durable, independently owned Tangent within this Space.</summary>
public sealed class Tangent : Entity<Tangent>
{
    public const string HomeKey = "home";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Motto { get; set; } = "";
    public string Accent { get; set; } = "";
    public string Artwork { get; set; } = "";
    public string OwnerParticipantId { get; set; } = "";
    public bool OpenToSignedIn { get; set; }
    public AccessMap Access { get; set; } = AccessMap.TangentDefaults();
    // Manual-approval admission queues durable join requests instead of granting membership.
    public bool ApprovalRequired { get; set; }
    // Classification policy: persisted zero-value defaults Everyone/Write keep every existing participant allowed.
    public ParticipationPreset ParticipationPreset { get; set; }
    public UndeclaredAccess UndeclaredAccess { get; set; }
    public bool SetupComplete { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long PolicyRevision { get; set; }

    public static Tangent Create(Space? space, string actorDid, string key, string name, string? description,
        string? motto, string? accent, string? artwork, DateTimeOffset now)
    {
        if (space is null || !space.IsOwner(actorDid) || !Participant.IsValidId(actorDid))
            throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the persisted space owner can create a Tangent.");
        CheckKey(key); CheckCard(name, description, motto, accent, artwork);
        return new Tangent
        {
            Id = key, Name = name.Trim(), Description = Clean(description), Motto = Clean(motto), Accent = Clean(accent), Artwork = Clean(artwork),
            OwnerParticipantId = actorDid, CreatedAt = now, UpdatedAt = now, PolicyRevision = 1, SetupComplete = true
        };
    }

    internal static Tangent CreateForAuthorizedActor(Space space, string actorDid, string ownerDid, string key,
        string name, string? description, string? motto, string? accent, string? artwork, DateTimeOffset now)
    {
        if (!space.IsOwner(ownerDid) || !Participant.IsValidId(actorDid))
            throw new TangentRuleViolation(TangentDenial.Forbidden, "The server owner must authorize Tangent creation.");
        CheckKey(key); CheckCard(name, description, motto, accent, artwork);
        return new Tangent { Id = key, Name = name.Trim(), Description = Clean(description), Motto = Clean(motto),
            Accent = Clean(accent), Artwork = Clean(artwork), OwnerParticipantId = actorDid, CreatedAt = now, UpdatedAt = now,
            PolicyRevision = 1, SetupComplete = true };
    }

    /// <summary>The first Tangent, established with the Host and named by its owner during onboarding.</summary>
    internal static Tangent Home(Space space) => new()
    {
        Id = HomeKey, Name = space.Name, OwnerParticipantId = space.OwnerParticipantId, OpenToSignedIn = true,
        CreatedAt = space.EstablishedAt, UpdatedAt = space.EstablishedAt, PolicyRevision = 1
    };

    public void Change(string actorDid, string? name, string? description, string? motto, string? accent, string? artwork, DateTimeOffset now)
    {
        if (!IsOwner(actorDid)) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the Tangent owner can change its card.");
        CheckCard(name ?? Name, description ?? Description, motto ?? Motto, accent ?? Accent, artwork ?? Artwork);
        Name = (name ?? Name).Trim(); Description = Clean(description ?? Description); Motto = Clean(motto ?? Motto);
        Accent = Clean(accent ?? Accent); Artwork = Clean(artwork ?? Artwork); UpdatedAt = now; PolicyRevision = checked(PolicyRevision + 1);
        SetupComplete = true;
    }

    internal void ChangeAuthorized(string actorDid, bool authorized, string? name, string? description, string? motto,
        string? accent, string? artwork, DateTimeOffset now)
    {
        if (!authorized) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only an authorized Tangent administrator can change its card.");
        CheckCard(name ?? Name, description ?? Description, motto ?? Motto, accent ?? Accent, artwork ?? Artwork);
        Name = (name ?? Name).Trim(); Description = Clean(description ?? Description); Motto = Clean(motto ?? Motto);
        Accent = Clean(accent ?? Accent); Artwork = Clean(artwork ?? Artwork); UpdatedAt = now; PolicyRevision = checked(PolicyRevision + 1); SetupComplete = true;
    }

    public bool IsOwner(string? participantId) => string.Equals(OwnerParticipantId, participantId, StringComparison.Ordinal);

    public void ChangeAccess(string actorDid, AccessMap access, DateTimeOffset now, bool authorized = false)
    {
        if (!IsOwner(actorDid) && !authorized) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only a Tangent administrator can change access.");
        try { Access = (access ?? throw new ArgumentNullException(nameof(access))).NormalizeForTangent(); }
        catch (ArgumentException invalid) { throw Invalid(invalid.Message); }
        UpdatedAt = now;
        PolicyRevision = checked(PolicyRevision + 1);
    }

    /// <summary>The one admission the stored open and approval bits express together.</summary>
    public TangentAdmission EffectiveAdmission => ApprovalRequired ? TangentAdmission.Approval
        : OpenToSignedIn ? TangentAdmission.Open : TangentAdmission.Invite;

    public void ChangeParticipationPolicy(string actorDid, TangentAdmission admission, ParticipationPreset preset,
        UndeclaredAccess undeclared, DateTimeOffset now)
    {
        if (!IsOwner(actorDid)) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the Tangent owner can change its participation policy.");
        if (!Enum.IsDefined(admission) || !Enum.IsDefined(preset) || !Enum.IsDefined(undeclared))
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "Choose a defined admission, preset, and undeclared access.");
        OpenToSignedIn = admission == TangentAdmission.Open;
        ApprovalRequired = admission == TangentAdmission.Approval;
        ParticipationPreset = preset;
        UndeclaredAccess = undeclared;
        UpdatedAt = now;
        PolicyRevision = checked(PolicyRevision + 1);
        SetupComplete = true;
    }

    /// <summary>Conversation rights from an explicit declaration. Never fetches labels or infers a classification.</summary>
    public (bool Read, bool Write) ParticipationRights(ParticipantClassification classification)
    {
        if (!Enum.IsDefined(classification))
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "The participant classification is not defined.");
        if (ParticipationPreset == ParticipationPreset.Everyone)
            return classification switch
            {
                // Declared participants are always welcome under Everyone; the stored undeclared access is honored too.
                ParticipantClassification.Human => (true, true),
                ParticipantClassification.Agent => (true, true),
                _ => UndeclaredRights()
            };
        var human = ParticipationPreset switch
        {
            ParticipationPreset.HumansOnly => (true, true),
            ParticipationPreset.HumansWriteAgentsRead => (true, true),
            ParticipationPreset.HumansReadAgentsWrite => (true, false),
            ParticipationPreset.AgentsOnly => (false, false),
            _ => throw new TangentRuleViolation(TangentDenial.InvalidInput, "The participation preset is not defined.")
        };
        var agent = ParticipationPreset switch
        {
            ParticipationPreset.AgentsOnly => (true, true),
            ParticipationPreset.HumansReadAgentsWrite => (true, true),
            ParticipationPreset.HumansWriteAgentsRead => (true, false),
            ParticipationPreset.HumansOnly => (false, false),
            _ => throw new TangentRuleViolation(TangentDenial.InvalidInput, "The participation preset is not defined.")
        };
        return classification switch
        {
            ParticipantClassification.Human => human,
            ParticipantClassification.Agent => agent,
            _ => UndeclaredRights()
        };
    }

    private (bool Read, bool Write) UndeclaredRights() => UndeclaredAccess switch
    {
        UndeclaredAccess.Write => (true, true),
        UndeclaredAccess.Read => (true, false),
        _ => (false, false)
    };

    public bool CanParticipate(string? participantId, TangentMembership? membership)
    {
        if (membership is not null && (membership.TangentKey != Id || membership.ParticipantId != participantId
            || membership.Id != TangentMembership.Key(Id, participantId ?? "") || !Enum.IsDefined(membership.Role)))
            throw new TangentRuleViolation(TangentDenial.MembershipMismatch, "The membership does not belong to this Tangent and participant.");
        if (IsOwner(participantId)) return true;
        // A durable removal is an explicit override, including for an otherwise open home.
        if (membership?.Role == TangentRole.Removed) return false;
        // Left is a voluntary departure, not a ban: open admission still applies, closed tangents need a new grant.
        return membership?.Role is TangentRole.Member or TangentRole.Admin or TangentRole.Reader
            || OpenToSignedIn && participantId is not null && Participant.IsValidId(participantId);
    }

    public static void CheckKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64 || key[0] == '-' || key[^1] == '-'
            || key.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-'))
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "A Tangent key must contain 1–64 lowercase letters, digits or interior hyphens.");
    }

    private static void CheckCard(string? name, string? description, string? motto, string? accent, string? artwork)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120) throw Invalid("A Tangent name must contain 1–120 characters.");
        if ((description?.Length ?? 0) > 1000) throw Invalid("A Tangent description can contain at most 1000 characters.");
        if ((motto?.Length ?? 0) > 240) throw Invalid("A Tangent motto can contain at most 240 characters.");
        if ((accent?.Length ?? 0) > 64) throw Invalid("A Tangent accent can contain at most 64 characters.");
        if ((artwork?.Length ?? 0) > 2048 || artwork?.Any(char.IsWhiteSpace) == true) throw Invalid("Artwork must be a short local reference or URL without whitespace.");
    }

    private static string Clean(string? value) => value?.Trim() ?? "";
    private static TangentRuleViolation Invalid(string message) => new(TangentDenial.InvalidInput, message);
}
