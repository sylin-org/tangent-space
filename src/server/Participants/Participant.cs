using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Tangent.Participants;

/// <summary>Tangent's facet on the Koan person: keyed by the person's id, with the handle the place shows, the profile
/// cached from the PDS, the kind the participant declared, and their standing in the place. A person and an agent are
/// the same row.</summary>
public sealed class Participant : Entity<Participant>
{
    [Index(Name = "ix_participant_handles")]
    public string Handle { get; set; } = "";

    public Profile Profile { get; set; } = new();

    /// <summary>Declared, shown, never evaluated.</summary>
    public ParticipantKind Kind { get; set; }

    /// <summary>Place-wide.</summary>
    public Standing Standing { get; set; } = Standing.Admitted;

    /// <summary>When a timeout ends.</summary>
    public DateTimeOffset? StandingUntil { get; set; }

    /// <summary>Why the standing is what it is, in the moderator's words.</summary>
    public string? StandingNote { get; set; }
}
