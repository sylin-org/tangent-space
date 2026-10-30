using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>Tangent's facet on the Koan person: keyed by the person's id, with the handle the place shows, the profile
/// cached from the public profile, the participant's kind with where it came from, and their standing in the place. A
/// person and an agent are the same row. Signing in through atproto makes the person's id their DID, so the id is the DID the profile is read
/// for. The profile job runs on this row: one bounded read of the public profile, handed to the writer.</summary>
[JobIdempotent(nameof(Id))]
public sealed class Participant : Entity<Participant>, IKoanJob<Participant>
{
    [Index(Name = "ix_participant_handles")]
    public string Handle { get; set; } = "";

    public Profile Profile { get; set; } = new();

    /// <summary>Shown, never evaluated: an agent when the account labels itself a bot on its own profile, a person
    /// otherwise, unless the participant chose it themselves.</summary>
    public ParticipantKind Kind { get; set; }

    /// <summary>Where <see cref="Kind"/> came from; the profile's label never overrides the participant's own
    /// choice.</summary>
    public KindSource KindSource { get; set; } = KindSource.ProfileLabel;

    /// <summary>Place-wide.</summary>
    public Standing Standing { get; set; } = Standing.Admitted;

    /// <summary>When a timeout ends.</summary>
    public DateTimeOffset? StandingUntil { get; set; }

    /// <summary>Why the standing is what it is, in the moderator's words.</summary>
    public string? StandingNote { get; set; }

    /// <summary>The participant a person becomes at their first sign-in: a person until their profile says
    /// otherwise, with the standing the place's admission gives — admitted where it says join, applied where it says
    /// apply.</summary>
    public static Participant Arrive(string personId, string handle, Admission admission)
    {
        if (!personId.StartsWith("did:", StringComparison.Ordinal))
            throw new Refusal("A participant signs in with an atproto identity, so the person's id is a DID.");
        return new Participant
        {
            Id = personId,
            Handle = handle,
            Kind = ParticipantKind.Person,
            KindSource = KindSource.ProfileLabel,
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

    /// <summary>Gives up the handle, which another participant's sign-in verified as theirs; the participant shows their
    /// DID until their own next sign-in verifies one.</summary>
    public void HandleTaken() => Handle = "";

    /// <summary>Whether the cached profile was captured less than <see cref="Profile.Lifetime"/> before
    /// <paramref name="now"/>.</summary>
    public bool ProfileFresh(DateTimeOffset now) => Profile.CapturedAt is { } captured && now - captured < Profile.Lifetime;

    /// <summary>Caches what the profile service answered, with the time of capture, and takes the kind its bot
    /// self-label says unless the participant chose their kind themselves. The handle is not the profile's to
    /// change.</summary>
    public void Capture(ProfileAnswer answer, DateTimeOffset at)
    {
        if (answer.Did != Id) throw new Refusal($"The profile answered is {answer.Did}'s, not {Id}'s.");
        if (KindSource == KindSource.ProfileLabel) Kind = answer.AgentLabel ? ParticipantKind.Agent : ParticipantKind.Person;
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
