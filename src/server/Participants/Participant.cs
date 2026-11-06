using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>Tangent's facet on the Koan person: keyed by the person's id, which is Koan's own and names no account, with
/// the DID of the one Bluesky account they are bound to now (<see cref="Did"/>), that account's verified handle, the
/// profile cached from its public profile, the participant's kind with where it came from, and their standing in the
/// place. A person and an agent are the same row. The profile job runs on this row: one bounded read of the bound
/// account's public profile, sent as a command.</summary>
[JobIdempotent(nameof(Id))]
public sealed class Participant : Entity<Participant>, IKoanJob<Participant>
{
    /// <summary>The DID of the Bluesky account the participant is bound to now, their external identity: taken from the
    /// session at every sign-in and proof. Their Posts each keep the DID they were written as
    /// (<see cref="Posts.Post.AuthorDid"/>).</summary>
    [Index(Name = "ix_participant_dids")]
    public string Did { get; set; } = "";

    [Index(Name = "ix_participant_handles")]
    public string Handle { get; set; } = "";

    public Profile Profile { get; set; } = new();

    /// <summary>The handle the place shows them by: the one their row keeps, else their DID.</summary>
    public string ShownHandle() => Handle.Length > 0 ? Handle : Did;

    /// <summary>The name the place shows them by: the display name from their profile, else <see cref="ShownHandle"/>.
    /// Read live wherever they are named, so a rename shows everywhere at once.</summary>
    public string ShownName() => Profile.DisplayName is { Length: > 0 } name ? name : ShownHandle();

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

    /// <summary>When the participant applied, while their application is waiting or declined; null once they are
    /// admitted, and for everyone who joined. The applications list reads it newest first.</summary>
    [Index(Name = "ix_participant_applications")]
    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>The log's sequence of the participant's arrival, where "since you were here" begins for them until they
    /// say what they have seen (<see cref="Events.Unread.Floor"/>).</summary>
    public long ArrivedSeq { get; set; }

    /// <summary>The log's sequence of the arrival that made the participant an applicant, while their application waits;
    /// null for everyone else, and once it is admitted or declined. The applications that wait are one range of
    /// <c>ix_participant_waiting</c>, newest first, which is how they reach whoever decides them.</summary>
    [Index(Name = "ix_participant_waiting")]
    public long? WaitingSeq { get; set; }

    /// <summary>The refusal a decision meets for a participant whose standing is not an application's.</summary>
    public static readonly Reason NotAnApplication = Reason.Conflict("Only an application waiting or declined is decided.");

    /// <summary>The refusal a timeout, a ban, a lift or a change of roles meets for an applicant, waiting or declined.</summary>
    public static readonly Reason NotAdmitted = Reason.Conflict("An applicant is admitted or declined among the applications before anything else changes their standing or roles.");

    /// <summary>The refusal a timeout meets for a banned participant.</summary>
    public static readonly Reason BannedFirst = Reason.Conflict("They are banned: lift the ban before timing them out.");

    /// <summary>The refusal a timeout meets whose end is not after its start.</summary>
    public static readonly Reason TimeoutEnds = Reason.Invalid("A timeout ends after it begins.");

    /// <summary>The refusal a sign-in meets that carries no atproto identity.</summary>
    public static readonly Reason NoAccount = Reason.Invalid("A participant signs in with a Bluesky account, so the sign-in carries its DID.");

    /// <summary>The participant a person becomes at their first sign-in, at <paramref name="at"/>, bound to the account
    /// <paramref name="did"/>: a person until their profile says otherwise, with the standing the place's admission gives —
    /// admitted where it says join, applied where it says apply, from then.</summary>
    public static Participant Arrive(string personId, string did, string handle, Admission admission, DateTimeOffset? at = null)
    {
        if (!IsDid(did)) throw new Refusal(NoAccount);
        return new Participant
        {
            Id = personId,
            Did = did,
            Handle = handle,
            Kind = ParticipantKind.Person,
            KindSource = KindSource.ProfileLabel,
            Standing = admission switch
            {
                Admission.Join => Standing.Admitted,
                Admission.Apply => Standing.Applied,
                _ => throw new Refusal(Reason.Invalid($"The place's admission {admission} admits no one."))
            },
            AppliedAt = admission == Admission.Apply ? at ?? DateTimeOffset.UtcNow : null
        };
    }

    /// <summary>Records that the participant's arrival took the log's sequence <paramref name="seq"/>: what is new to them
    /// begins there, and where they applied, their application waits at it.</summary>
    public void Arrived(long seq)
    {
        ArrivedSeq = seq;
        WaitingSeq = Standing == Standing.Applied ? seq : null;
    }

    /// <summary>Admits an applicant, waiting or declined: they stand admitted and their application is closed. False,
    /// with nothing changed, for one who stands admitted at <paramref name="now"/>, a timeout that has ended among them;
    /// a timed-out or banned participant is refused with <see cref="NotAnApplication"/>.</summary>
    public bool Admit(DateTimeOffset now)
    {
        var standing = StandingAt(now);
        if (standing == Standing.Admitted) return false;
        if (standing is not (Standing.Applied or Standing.Declined)) throw new Refusal(NotAnApplication);
        Standing = Standing.Admitted;
        AppliedAt = null;
        WaitingSeq = null;
        return true;
    }

    /// <summary>Declines a waiting application: the applicant keeps reading what a visitor reads, the application stays
    /// listed as declined and no longer waits, and a later <see cref="Admit"/> reverses it. False, with nothing changed,
    /// for one already declined; anyone else is refused with <see cref="NotAnApplication"/>, read at
    /// <paramref name="now"/>.</summary>
    public bool Decline(DateTimeOffset now)
    {
        var standing = StandingAt(now);
        if (standing == Standing.Declined) return false;
        if (standing != Standing.Applied) throw new Refusal(NotAnApplication);
        Standing = Standing.Declined;
        WaitingSeq = null;
        return true;
    }

    /// <summary>The standing in force at <paramref name="now"/>, which every decision about the participant reads: a
    /// timeout whose end has come reads as admitted, and the row keeps it as it was written.</summary>
    public Standing StandingAt(DateTimeOffset now)
        => Standing == Standing.TimedOut && StandingUntil is { } until && until <= now ? Standing.Admitted : Standing;

    /// <summary>Times the participant out until <paramref name="until"/>: until then they read and report, and post,
    /// reply and start nothing. False,
    /// with nothing changed, where they are already timed out until that instant; an applicant is refused with
    /// <see cref="NotAdmitted"/>, a banned participant with <see cref="BannedFirst"/>, and an end not after
    /// <paramref name="now"/> with <see cref="TimeoutEnds"/>.</summary>
    public bool TimeOut(DateTimeOffset until, DateTimeOffset now)
    {
        if (until <= now) throw new Refusal(TimeoutEnds);
        var standing = StandingAt(now);
        if (standing is Standing.Applied or Standing.Declined) throw new Refusal(NotAdmitted);
        if (standing == Standing.Banned) throw new Refusal(BannedFirst);
        if (standing == Standing.TimedOut && StandingUntil == until) return false;
        Standing = Standing.TimedOut;
        StandingUntil = until;
        return true;
    }

    /// <summary>Bans the participant: the place is absent to them until someone lifts it. False, with nothing changed,
    /// for one already banned; an applicant is refused with <see cref="NotAdmitted"/>.</summary>
    public bool Ban(DateTimeOffset now)
    {
        var standing = StandingAt(now);
        if (standing is Standing.Applied or Standing.Declined) throw new Refusal(NotAdmitted);
        if (standing == Standing.Banned) return false;
        Standing = Standing.Banned;
        StandingUntil = null;
        return true;
    }

    /// <summary>Lifts a timeout or a ban: the participant stands admitted again. False, with nothing changed, for one
    /// who stands admitted, a timeout that has ended among them; an applicant is refused with
    /// <see cref="NotAdmitted"/>.</summary>
    public bool Lift(DateTimeOffset now)
    {
        var standing = StandingAt(now);
        if (standing is Standing.Applied or Standing.Declined) throw new Refusal(NotAdmitted);
        if (standing == Standing.Admitted) return false;
        Standing = Standing.Admitted;
        StandingUntil = null;
        return true;
    }

    /// <summary>The DID of the account the participant <paramref name="id"/> is bound to now, in one keyed read; empty
    /// for an id that is no participant's.</summary>
    public static async Task<string> DidOf(string id, CancellationToken ct) => (await Get(id, ct))?.Did ?? "";

    /// <summary>Whether <paramref name="text"/> is a participant's id: Koan's, a GUID.</summary>
    public static bool IsId(string? text) => text is { Length: 36 } && Guid.TryParseExact(text, "D", out _);

    /// <summary>The page on Bluesky of the account <paramref name="did"/>, by DID, never by a handle that may change.</summary>
    public static string Elsewhere(string did) => "https://bsky.app/profile/" + did;

    /// <summary>Whether <paramref name="text"/> is a DID as atproto writes one.</summary>
    public static bool IsDid(string? text) => text is { Length: > 4 and <= Posts.Mentions.IdentityLimit } && text.StartsWith("did:", StringComparison.Ordinal);

    /// <summary>Takes the account the connector verified at this sign-in, its DID and its handle, empty when it verified
    /// none. A sign-in as another account than before binds them to it: the profile cached for the earlier one goes, so
    /// the next capture reads the new one's. True when anything changed.</summary>
    public bool Verified(string did, string handle)
    {
        if (!IsDid(did)) throw new Refusal(NoAccount);
        if (handle.Length > SignIn.HandleLimit) throw new Refusal(Reason.Invalid("A handle is at most 253 characters."));
        if (did == Did && handle == Handle) return false;
        if (did != Did)
        {
            Did = did;
            Profile = new Profile();
        }

        Handle = handle;
        return true;
    }

    /// <summary>Gives up the handle, which another participant's sign-in verified as theirs; the participant shows their
    /// DID until their own next sign-in verifies one.</summary>
    public void HandleTaken() => Handle = "";

    /// <summary>Whether the cached profile was captured less than <see cref="Profile.Lifetime"/> before
    /// <paramref name="now"/>.</summary>
    public bool ProfileFresh(DateTimeOffset now) => Profile.CapturedAt is { } captured && now - captured < Profile.Lifetime;

    /// <summary>Caches what the profile service answered for the account they are bound to, with the time of capture, and
    /// takes the kind its bot self-label says unless the participant chose their kind themselves. The handle is not the
    /// profile's to change.</summary>
    public void Capture(ProfileAnswer answer, DateTimeOffset at)
    {
        if (answer.Did != Did) throw new Refusal(Reason.Invalid($"The profile answered is {answer.Did}'s, not {Did}'s."));
        if (KindSource == KindSource.ProfileLabel) Kind = answer.AgentLabel ? ParticipantKind.Agent : ParticipantKind.Person;
        Profile = new Profile
        {
            DisplayName = answer.DisplayName,
            Description = answer.Description,
            Avatar = answer.Avatar,
            CapturedAt = at
        };
    }

    /// <summary>The profile job: Koan runs it after a submission, outside any command.</summary>
    public static Task Execute(Participant participant, JobContext context, CancellationToken ct)
        => ProfileCapture.Run(participant, context, ct);
}
