using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>Tangent's facet on the Koan person: keyed by the person's id, with the handle the place shows, the profile
/// cached from the public profile, the kind the participant declared, and their standing in the place. A person and an agent are
/// the same row. Signing in through atproto makes the person's id their DID, so the id is the DID the profile is read
/// for. The profile job runs on this row: one bounded read of the public profile, handed to the writer.</summary>
[JobIdempotent(nameof(Id))]
public sealed class Participant : Entity<Participant>, IKoanJob<Participant>
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

    /// <summary>The participant a person becomes at their first arrival: the kind they declared, and the standing the
    /// place's admission gives — admitted where it says join, applied where it says apply.</summary>
    public static Participant Arrive(string personId, string handle, ParticipantKind kind, Admission admission)
    {
        if (!personId.StartsWith("did:", StringComparison.Ordinal))
            throw new Refusal("A participant signs in with an atproto identity, so the person's id is a DID.");
        if (!Enum.IsDefined(kind)) throw new Refusal("Declare person or agent.");
        return new Participant
        {
            Id = personId,
            Handle = handle,
            Kind = kind,
            Standing = admission switch
            {
                Admission.Join => Standing.Admitted,
                Admission.Apply => Standing.Applied,
                _ => throw new Refusal($"The place's admission {admission} admits no one.")
            }
        };
    }

    /// <summary>Takes the handle the connector verified at this sign-in; empty when it verified none. True when it
    /// changed.</summary>
    public bool Verified(string handle)
    {
        if (handle.Length > SignIn.HandleLimit) throw new Refusal("A handle is at most 253 characters.");
        if (handle == Handle) return false;
        Handle = handle;
        return true;
    }

    /// <summary>Whether the cached profile was captured less than <see cref="Profile.Lifetime"/> before
    /// <paramref name="now"/>.</summary>
    public bool ProfileFresh(DateTimeOffset now) => Profile.CapturedAt is { } captured && now - captured < Profile.Lifetime;

    /// <summary>Caches what the profile service answered, with the time of capture. The handle is not the profile's to
    /// change.</summary>
    public void Capture(ProfileAnswer answer, DateTimeOffset at)
    {
        if (answer.Did != Id) throw new Refusal($"The profile answered is {answer.Did}'s, not {Id}'s.");
        Profile = new Profile
        {
            DisplayName = answer.DisplayName,
            Description = answer.Description,
            Avatar = answer.Avatar,
            CapturedAt = at
        };
    }

    /// <summary>The profile job: Koan runs it after a submission, outside the writer.</summary>
    public static Task Execute(Participant participant, JobContext context, CancellationToken ct)
        => ProfileCapture.Run(participant, context, ct);
}
