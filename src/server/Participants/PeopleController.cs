using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Koan.Web.Attributes;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>What timing someone out sends, as a form or one JSON object.</summary>
public sealed record TimeOutInput(
    [Description(Holds.Participant)] string? Participant = null,
    [Description(Holds.TimeOutFor)] string? For = null,
    [Description(Holds.Back)] string? Back = null);

/// <summary>What changing someone's standing sends, as a form or one JSON object.</summary>
public sealed record StandingInput(
    [Description(Holds.Participant)] string? Participant = null,
    [Description(Holds.Back)] string? Back = null);

/// <summary>What giving someone a role sends, as a form or one JSON object.</summary>
public sealed record GiveRoleInput(
    [Description(Holds.Participant)] string? Participant = null,
    [Description(Holds.GivenRole)] string? Role = null,
    [Description(Holds.Back)] string? Back = null);

/// <summary>What taking a role from someone sends, as a form or one JSON object.</summary>
public sealed record TakeRoleInput(
    [Description(Holds.Participant)] string? Participant = null,
    [Description(Holds.TakenRole)] string? Role = null,
    [Description(Holds.Back)] string? Back = null);

/// <summary>The place's people, a section of its settings at <c>/settings/people</c>, for whoever holds
/// <see cref="Bit.ManageStanding"/> or <see cref="Bit.ManageRoles"/>. Its <c>GET</c> is one bounded page of them
/// (<see cref="People.Read"/>), one resource both ways (<see cref="PeoplePage"/>), continued from the sealed cursor in
/// <c>after</c> and narrowed to the handles that start with <c>handle</c>; a cursor this server did not seal is refused
/// with <c>400</c>, and a reader who may not read them reads the gate with no one listed, answered <c>401</c> to sign in
/// and <c>403</c> otherwise. Its <c>POST</c>s take the participant's id in <c>participant</c>, as a form or a JSON
/// object, and send one command: <c>time-out</c> sends <see cref="TimeOut"/> for the duration in <c>for</c>;
/// <c>ban</c>, <see cref="Ban"/>; <c>lift</c>, <see cref="Lift"/>, after which the participant joins the member role
/// where they lack it (<see cref="MemberRole"/>); <c>give</c> and <c>take</c>, <see cref="ChangeRole"/> for the role in
/// <c>role</c>. A form is sent back to the page in <c>back</c> — this one, or the participant's profile, where the same
/// forms are — and a JSON request reads the participant as this page now lists them; a refusal reads the page again with
/// the reason above the list, or a problem. A reader for whom the place does not exist receives what an address with
/// nothing there receives.</summary>
public sealed class PeopleController(KoanWebBaseUrl baseUrl, RoleCollection roles, IDataProtectionProvider protection) : ControllerBase
{
    public const string Path = "/settings/people";

    public const string TimeOutPath = Path + "/time-out";

    public const string BanPath = Path + "/ban";

    public const string LiftPath = Path + "/lift";

    public const string GivePath = Path + "/give";

    public const string TakePath = Path + "/take";

    /// <summary>The field that names the participant changed.</summary>
    public const string ParticipantField = "participant";

    /// <summary>The field that says how long a timeout lasts: one of <see cref="People.Durations"/>.</summary>
    public const string ForField = "for";

    /// <summary>The field that names the role given or taken, by its word (<see cref="Roles.Word(string)"/>): one of a
    /// participant's <c>give</c> or <c>take</c>.</summary>
    public const string RoleField = "role";

    /// <summary>The field that names the page a form returns to.</summary>
    public const string BackField = "back";

    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    /// <summary>The query parameter that carries the start of a handle the page is narrowed to.</summary>
    public const string HandleQuery = "handle";

    [HttpGet(Path)]
    [Tool(Name = "read_people")]
    [Description("Find the participants admitted here, a page at a time, with what you may change of each, for whoever holds ManageStanding or ManageRoles. Follow with time_out, ban, lift, give_role or take_role where one's entry allows it.")]
    public async Task<IActionResult> Get([FromQuery(Name = After), Description(Holds.After)] string? after, [FromQuery(Name = HandleQuery), Description(Holds.HandleStart)] string? handle,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));

        People.Cursor? cursor = null;
        if (after is not null && (cursor = People.Cursor.Open(after, protection)) is null)
            return new Problem(StatusCodes.Status400BadRequest, "Not a page of the people",
                $"The {After} parameter is the cursor a page of the people names as its next.").ToResult();

        var page = await Page(place, personId, People.Start(handle), cursor, ct);
        return page.Gate is { } gate ? new ObjectResult(page) { StatusCode = PostWrites.Status(gate) } : Ok(page);
    }

    [HttpPost(TimeOutPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "time_out", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Time a participant out for an hour, a day or a week, for whoever holds ManageStanding: they read and report, and post nothing, and they are told. lift ends it early.")]
    public Task<IActionResult> PostTimeOut(TimeOutInput input, CancellationToken ct) => Change(input.Participant, input.Back, (actor, participant) =>
        People.Length(input.For) is { } length
            ? new TimeOut(actor, participant, DateTimeOffset.UtcNow + length)
            : null, People.NotADuration, ct);

    [HttpPost(BanPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "ban", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Ban a participant, for whoever holds ManageStanding: the place no longer exists for them, and they are told. lift reverses it.")]
    public Task<IActionResult> PostBan(StandingInput input, CancellationToken ct) => Change(input.Participant, input.Back, (actor, participant) => new Ban(actor, participant), "", ct);

    [HttpPost(LiftPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "lift", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Lift a participant's timeout or ban, so they take part again, for whoever holds ManageStanding.")]
    public Task<IActionResult> PostLift(StandingInput input, CancellationToken ct) => Change(input.Participant, input.Back, (actor, participant) => new Lift(actor, participant), "", ct);

    [HttpPost(GivePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "give_role", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Give a participant a role listed in their give in read_people, for whoever holds ManageRoles and manages the role. They hold what it grants at once, power over others among it.")]
    public Task<IActionResult> PostGive(GiveRoleInput input, CancellationToken ct) => Change(input.Participant, input.Back, (actor, participant) =>
        Roles.KeyOf(input.Role) is { } role ? new ChangeRole(actor, participant, role, Give: true) : null,
        People.NotARole, ct);

    [HttpPost(TakePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "take_role", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Take from a participant a role listed in their take in read_people, for whoever holds ManageRoles and manages the role. They lose what it granted at once.")]
    public Task<IActionResult> PostTake(TakeRoleInput input, CancellationToken ct) => Change(input.Participant, input.Back, (actor, participant) =>
        Roles.KeyOf(input.Role) is { } role ? new ChangeRole(actor, participant, role, Give: false) : null,
        People.NotARole, ct);

    /// <summary>One change: the place; the command <paramref name="command"/> builds for who sent it and
    /// whom it names, or <paramref name="unsent"/> where what was sent names none; then the command's outcome.</summary>
    private async Task<IActionResult> Change(string? named, string? back, Func<string, string, ICommand<Participant>?> command, Reason unsent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));

        if (command(personId ?? "", named ?? "") is not { } sent) return await Refused(personId, unsent, ct);
        var outcome = await sent.Send();
        switch (outcome)
        {
            case Outcome<Participant>.Done { Result: var participant }:
                return await Changed(participant, personId, back, ct);
            case Outcome<Participant>.Refused { Reason: var reason }:
                return await Refused(personId, reason, ct);
            default:
                return PostWrites.Answer(HttpContext, outcome);
        }
    }

    /// <summary>A change that committed, or found nothing to change: a form is sent back where it came from; a JSON
    /// request reads the participant as the people now list them for the reader.</summary>
    private async Task<IActionResult> Changed(Participant participant, string? personId, string? sentBack, CancellationToken ct)
    {
        if (!PostWrites.WantsJson(Request))
        {
            var back = Back(sentBack);
            Response.Headers.Location = BaseAddress() + back[(baseUrl.PathBase.Value?.Length ?? 0)..];
            return StatusCode(StatusCodes.Status303SeeOther);
        }

        var place = await Capabilities.Load(personId, null, null, ct);
        var lister = await Lister.Load(roles, place, personId, BaseAddress(), ct);
        return new ObjectResult(await lister.Row(participant, ct)) { ContentTypes = { "application/json" } };
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the page again, read afresh,
    /// with the reason above the list.</summary>
    private async Task<IActionResult> Refused(string? personId, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return PostWrites.Refused(HttpContext, reason);

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));
        var page = await Page(place, personId, null, null, ct);
        if (page.Gate is null) page = page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = PostWrites.Status(reason) };
    }

    private async Task<PeoplePage> Page(Capabilities place, string? personId, string? start, People.Cursor? cursor, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var narrowed = start is null ? "" : HandleQuery + "=" + Uri.EscapeDataString(start);
        var here = baseUrl.PathBase + Path + Query(narrowed, cursor is null ? "" : After + "=" + Uri.EscapeDataString(cursor.Seal(protection)));
        var people = baseAddress + Path;
        var gate = ReadGate(place)?.Word;
        var read = gate is null ? await People.Read(start, cursor, ct) : new People.Page([], null);
        var lister = await Lister.Load(roles, place, personId, baseAddress, ct);
        var listed = new List<PeoplePage.Listed>(read.Participants.Count);
        foreach (var participant in read.Participants) listed.Add(await lister.Row(participant, ct));
        return new PeoplePage(
            TangentSpaceSettingsController.Sections(place, baseAddress, TangentSpaceSettingsController.PeopleSection),
            listed,
            read.Next is { } next ? people + Query(narrowed, After + "=" + Uri.EscapeDataString(next.Seal(protection))) : null,
            cursor is not null,
            people + Query(narrowed, ""),
            start ?? "",
            people,
            Forms(place, baseAddress, here),
            place.Can,
            gate,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            lister.Space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct),
            DateOnly.FromDateTime(DateTime.UtcNow));
    }

    /// <summary>Why the reader of <paramref name="place"/> may not read the people: null where they hold
    /// <see cref="Bit.ManageStanding"/> or <see cref="Bit.ManageRoles"/>, the gate of <see cref="Bit.ManageStanding"/>
    /// otherwise.</summary>
    public static Reason? ReadGate(Capabilities place)
        => place.Holds(Bit.ManageStanding) || place.Holds(Bit.ManageRoles) ? null : place.Gate(Bit.ManageStanding);

    /// <summary>The forms of the changes the reader of <paramref name="place"/> may make, under
    /// <paramref name="baseAddress"/>, returning to <paramref name="back"/>,
    /// a path under the base URL; null where they may not read the people.</summary>
    public static PeopleForms? Forms(Capabilities place, string baseAddress, string back)
    {
        if (ReadGate(place) is not null) return null;
        var standing = place.Holds(Bit.ManageStanding);
        var roles = place.Holds(Bit.ManageRoles);
        var durations = People.Durations.Select(duration => duration.Word).ToList();
        Form Changing(string path, string whose, params Form.Field[] more) => Form.Posting(baseAddress + path,
        [
            new Form.Field(ParticipantField, $"The id of a participant listed here with {whose}."),
            .. more,
            new Form.Field(BackField, Holds.Back, back)
        ]);
        return new PeopleForms(
            standing ? Changing(TimeOutPath, "time-out in their can", new Form.Field(ForField, "How long the timeout lasts: " + Words(durations) + ".")) : null,
            standing ? Changing(BanPath, "ban in their can") : null,
            standing ? Changing(LiftPath, "lift in their can") : null,
            roles ? Changing(GivePath, "the role in their give", new Form.Field(RoleField, Holds.GivenRole)) : null,
            roles ? Changing(TakePath, "the role in their take", new Form.Field(RoleField, Holds.TakenRole)) : null,
            durations);
    }

    /// <summary>Words joined as a sentence joins them: "hour, day or week".</summary>
    private static string Words(IReadOnlyList<string> words)
        => words.Count < 2 ? string.Concat(words) : string.Join(", ", words.Take(words.Count - 1)) + " or " + words[^1];

    /// <summary>The page a form returns to: the path it sent in <c>back</c> where that is under the base URL's, of visible
    /// characters, without scheme or authority, and the people's or a profile's; the people otherwise.</summary>
    private string Back(string? sent)
    {
        var people = baseUrl.PathBase + Path;
        if (sent is not { Length: > 0 } || sent[0] != '/' || sent.Length > 1 && sent[1] is '/' or '\\'
            || !sent.All(c => c is > ' ' and <= '~' and not '\\'))
            return people;
        var path = new PathString(sent.Split('?', '#')[0]);
        return path.StartsWithSegments(new PathString(people)) || path.StartsWithSegments(new PathString(baseUrl.PathBase + "/p")) ? sent : people;
    }

    /// <summary>A query string of the parts given, empty when none is.</summary>
    private static string Query(params string[] parts)
    {
        var given = parts.Where(part => part.Length > 0).ToList();
        return given.Count == 0 ? "" : "?" + string.Join("&", given);
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);

    /// <summary>What one reader may see and do of the people on one page, read once — the place's row, and one page of
    /// the roles in their order, the highest first, with what each grants — and then each participant's row from Koan's
    /// bag for them, with any role of theirs past that page read once.</summary>
    public sealed class Lister
    {
        private readonly RoleCollection _roles;
        private readonly Capabilities _reader;
        private readonly string? _readerId;
        private readonly IReadOnlyList<Role> _given;
        private readonly string _baseAddress;
        private readonly Dictionary<string, Role?> _read = new(StringComparer.Ordinal);
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

        private Lister(RoleCollection roles, Capabilities reader, string? readerId, TangentSpace space, IReadOnlyList<Role> read,
            string baseAddress)
        {
            _roles = roles;
            _reader = reader;
            _readerId = readerId;
            Space = space;
            // A row offers the roles given here lowest first.
            _given = read.Where(role => Roles.Given(role.Key)).OrderBy(role => role.Position).ToList();
            _baseAddress = baseAddress;
            foreach (var role in read) _read[role.Key] = role;
        }

        /// <summary>The place's row.</summary>
        public TangentSpace Space { get; }

        public static async Task<Lister> Load(RoleCollection roles, Capabilities reader, string? readerId, string baseAddress, CancellationToken ct)
        {
            var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
            var read = await roles.Page(pageSize: People.GivenRead, ct: ct);
            return new Lister(roles, reader, readerId, space, read.Items, baseAddress);
        }

        /// <summary>One participant as the people list them for the reader.</summary>
        public async Task<PeoplePage.Listed> Row(Participant participant, CancellationToken ct)
        {
            var bag = await _roles.Bag(participant.Id, authenticated: true, ct);
            var held = bag.Roles.Where(role => role is not (RoleTokens.Everyone or RoleTokens.Authenticated)).ToList();
            var names = new List<(int Position, string Name)>(held.Count);
            foreach (var key in held)
            {
                var role = await Read(key, ct);
                names.Add((role?.Position ?? 0, role?.Name ?? key));
            }

            var offer = People.Offered(_reader, _readerId, participant, bag, Space, _given, _now);
            var standing = participant.StandingAt(_now);
            var handle = participant.Handle.Length > 0 ? participant.Handle : participant.Id;
            return new PeoplePage.Listed(
                participant.Id,
                participant.Profile.DisplayName is { Length: > 0 } displayName ? displayName : handle,
                handle,
                ProfilePage.AddressOf(_baseAddress, handle),
                participant.Kind.ToString().ToLowerInvariant(),
                People.Word(standing),
                standing == Standing.TimedOut ? participant.StandingUntil : null,
                names.OrderByDescending(name => name.Position).ThenBy(name => name.Name, StringComparer.Ordinal).Select(name => name.Name).ToList(),
                offer.Can,
                offer.Give,
                offer.Take);
        }

        /// <summary>A role's row, read once per role; null where the role is gone.</summary>
        private async Task<Role?> Read(string key, CancellationToken ct)
        {
            if (_read.TryGetValue(key, out var role)) return role;
            role = await _roles.Get(key, ct);
            _read[key] = role;
            return role;
        }
    }
}
