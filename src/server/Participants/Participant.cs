using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Lifecycle;
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
    [Index(Name = "ix_participant_standings", Order = 1)]
    public string Handle { get; set; } = "";

    public Profile Profile { get; set; } = new();

    /// <summary>The handle the place shows them by: the one their row keeps, else their DID.</summary>
    public string ShownHandle() => Handle.Length > 0 ? Handle : Did;

    /// <summary>The name the place shows them by: the display name from their profile, else <see cref="ShownHandle"/>.
    /// Read live wherever they are named, so a rename shows everywhere at once.</summary>
    public string ShownName() => Profile.DisplayName is { Length: > 0 } name ? name : ShownHandle();

    /// <summary>Shown, never evaluated: an agent when the account labels itself a bot on its own profile or when the
    /// participant chose to be shown as one (<see cref="Choose"/>), a person otherwise.</summary>
    public ParticipantKind Kind { get; set; }

    /// <summary>Where <see cref="Kind"/> came from: the profile's label, or the participant's own choice of the agent
    /// mark. The label wins: an account that labels itself a bot is an agent here, whatever its holder chose.</summary>
    public KindSource KindSource { get; set; } = KindSource.ProfileLabel;

    /// <summary>Whether the account's own bot label makes them an agent, which they cannot take back here.</summary>
    public bool Labelled => Kind == ParticipantKind.Agent && KindSource == KindSource.ProfileLabel;

    /// <summary>Place-wide. Those who stand as one standing are one range of <c>ix_participant_standings</c>, in handle
    /// order.</summary>
    [Index(Name = "ix_participant_standings", Order = 0)]
    public Standing Standing { get; set; } = Standing.Admitted;

    /// <summary>When a timeout ends.</summary>
    public DateTimeOffset? StandingUntil { get; set; }

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

    /// <summary>The slugs of the Tangents the participant quieted: what is new in them stops counting on their door, while
    /// what is for them there still reaches them (<see cref="Tangents.QuietTangent"/>).</summary>
    public List<string> Quiet { get; set; } = [];

    /// <summary>Quiets the Tangent <paramref name="slug"/>, or with <paramref name="on"/> false lets it count again; false,
    /// with nothing changed, where it already stands so.</summary>
    public bool Quieten(string slug, bool on)
    {
        if (Quiet.Contains(slug) == on) return false;
        if (on) Quiet.Add(slug);
        else Quiet.Remove(slug);
        return true;
    }

    /// <summary>The refusal a change of roles meets for an applicant, waiting or declined.</summary>
    public static readonly Reason NotAdmitted = Reason.Conflict("An applicant is admitted among the applications before their roles change.");

    /// <summary>The refusal a timeout meets whose end is not after its start.</summary>
    public static readonly Reason TimeoutEnds = Reason.Invalid("A timeout ends after it begins.");

    /// <summary>The refusal a participant meets who takes back an agent mark their own account's bot label gives them.</summary>
    public static readonly Reason LabelledAgent = Reason.Conflict("labelled",
        "Your Bluesky account labels itself a bot, so you are shown as an agent here; take the label off on Bluesky to change it.");

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
    /// begins there, and where they applied, their application waits at it (<see cref="Holds"/>).</summary>
    public void Arrived(long seq) => ArrivedSeq = seq;

    /// <summary>The standing in force at <paramref name="now"/>, which every decision about the participant reads: a
    /// timeout whose end has come reads as admitted, and the row keeps it as it was written.</summary>
    public Standing StandingAt(DateTimeOffset now)
        => Standing == Standing.TimedOut && StandingUntil is { } until && until <= now ? Standing.Admitted : Standing;

    /// <summary>One way a standing changes: from <see cref="From"/> to <see cref="To"/>, with the word its button carries and,
    /// where the page confirms it, the sentence it confirms with.</summary>
    public sealed record Transition(Standing From, Standing To, string Button, string? Confirm = null);

    private const string BanConfirm = "This place disappears for them, every page and every stream, until you lift the ban.";

    /// <summary>What follows what, the one place that says it: an applicant is admitted or declined, and one declined is
    /// admitted later; someone admitted is timed out or banned; a timeout is lifted or turned into a ban; a ban is lifted. Koan
    /// refuses any other save of a standing (<see cref="TangentModule"/>), and a row's <c>can</c> and every button that changes
    /// a standing are drawn from it, read from the standing in force (<see cref="StandingAt"/>). Every row from admitted is a
    /// row from timed out, or leaves a timeout that has ended stored as it stands, so the stored standing Koan reads takes each
    /// change the standing in force offers.</summary>
    public static readonly IReadOnlyList<Transition> Standings =
    [
        new(Standing.Applied, Standing.Admitted, "Admit"),
        new(Standing.Applied, Standing.Declined, "Decline", "They keep reading what a visitor reads, and you can still admit them later."),
        new(Standing.Declined, Standing.Admitted, "Admit"),
        new(Standing.Admitted, Standing.TimedOut, "Time out",
            "They keep reading and reporting here, and post, reply and start nothing until it ends; you can lift it sooner."),
        new(Standing.Admitted, Standing.Banned, "Ban", BanConfirm),
        new(Standing.TimedOut, Standing.Admitted, "Lift the timeout"),
        new(Standing.TimedOut, Standing.Banned, "Ban", BanConfirm),
        new(Standing.Banned, Standing.Admitted, "Lift the ban")
    ];

    /// <summary>The changes <see cref="Standings"/> offers from the standing <paramref name="from"/>, in its order.</summary>
    public static IEnumerable<Transition> From(Standing from) => Standings.Where(transition => transition.From == from);

    /// <summary>The standings <paramref name="gives"/> give, by the word <c>to</c> takes: a row's <c>can</c>.</summary>
    public static IReadOnlyList<string> Can(IEnumerable<Transition> gives) => [.. gives.Select(transition => People.Word(transition.To))];

    /// <summary>What every participant's row holds, however it is written — an arrival, a change of standing, a seed —
    /// compiled as it is saved, as its lifecycle (<see cref="TangentModule"/>): a timeout's end only on one timed out, when
    /// they applied only on an applicant, waiting or declined, and <see cref="WaitingSeq"/> their arrival's sequence while
    /// they wait, null for everyone else.</summary>
    public static EntityLifecycleResult Holds(EntityLifecycleContext<Participant> context)
    {
        var participant = context.Current;
        if (participant.Standing != Standing.TimedOut) participant.StandingUntil = null;
        if (participant.Standing is not (Standing.Applied or Standing.Declined)) participant.AppliedAt = null;
        participant.WaitingSeq = participant.Standing == Standing.Applied ? participant.ArrivedSeq : null;
        return context.Proceed();
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
    /// the kind its bot self-label says: an agent wherever it says bot, whatever the participant chose; a person where it
    /// does not, unless they chose to be shown as an agent. The handle is not the profile's to change.</summary>
    public void Capture(ProfileAnswer answer, DateTimeOffset at)
    {
        if (answer.Did != Did) throw new Refusal(Reason.Invalid($"The profile answered is {answer.Did}'s, not {Did}'s."));
        if (answer.AgentLabel) (Kind, KindSource) = (ParticipantKind.Agent, KindSource.ProfileLabel);
        else if (KindSource == KindSource.ProfileLabel) Kind = ParticipantKind.Person;
        Profile = new Profile
        {
            DisplayName = answer.DisplayName,
            Description = answer.Description,
            Avatar = answer.Avatar,
            Banner = answer.Banner,
            CapturedAt = at
        };
    }

    /// <summary>The participant's own choice of how the place shows them: as an agent, which marks them so until they take
    /// it back, or as a person, which takes back their own mark and is refused with <see cref="LabelledAgent"/> where their
    /// account's bot label makes them an agent (<see cref="Labelled"/>). False, with nothing changed, where they are shown
    /// so already.</summary>
    public bool Choose(ParticipantKind kind)
    {
        if (kind == Kind) return false;
        if (kind == ParticipantKind.Person && Labelled) throw new Refusal(LabelledAgent);
        (Kind, KindSource) = kind == ParticipantKind.Agent ? (ParticipantKind.Agent, KindSource.OwnChoice) : (ParticipantKind.Person, KindSource.ProfileLabel);
        return true;
    }

    /// <summary>The profile job: Koan runs it after a submission, outside any command.</summary>
    public static Task Execute(Participant participant, JobContext context, CancellationToken ct)
        => ProfileCapture.Run(participant, context, ct);
}
