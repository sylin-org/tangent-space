using Koan.Identity.Roles;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Participants;

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
public sealed class PeopleController(KoanWebBaseUrl baseUrl, WriteLoop writer, RoleCollection roles, IDataProtectionProvider protection,
    ILogger<PeopleController> logger) : ControllerBase
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

    /// <summary>The field that names the role given or taken: one of <see cref="People.Given"/>.</summary>
    public const string RoleField = "role";

    /// <summary>The field that names the page a form returns to.</summary>
    public const string BackField = "back";

    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    /// <summary>The query parameter that carries the start of a handle the page is narrowed to.</summary>
    public const string HandleQuery = "handle";

    [HttpGet(Path)]
    public async Task<IActionResult> Get([FromQuery(Name = After)] string? after, [FromQuery(Name = HandleQuery)] string? handle,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));

        People.Cursor? cursor = null;
        if (after is not null && (cursor = People.Cursor.Open(after, protection)) is null)
            return new Problem(StatusCodes.Status400BadRequest, "Not a page of the people",
                $"The {After} parameter is the cursor a page of the people names as its next.").ToResult();

        var page = await Page(place, personId, People.Start(handle), cursor, ct);
        return page.Gate is { } gate ? new ObjectResult(page) { StatusCode = PostWrites.Status(gate) } : Ok(page);
    }

    [HttpPost(TimeOutPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public Task<IActionResult> PostTimeOut(CancellationToken ct) => Change((fields, actor, participant) =>
        People.Length(fields.GetValueOrDefault(ForField)) is { } length
            ? new TimeOut(actor, participant, DateTimeOffset.UtcNow + length)
            : null, People.NotADuration, ct);

    [HttpPost(BanPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public Task<IActionResult> PostBan(CancellationToken ct) => Change((_, actor, participant) => new Ban(actor, participant), "", ct);

    [HttpPost(LiftPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public Task<IActionResult> PostLift(CancellationToken ct) => Change((_, actor, participant) => new Lift(actor, participant), "", ct);

    [HttpPost(GivePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public Task<IActionResult> PostGive(CancellationToken ct) => Change((fields, actor, participant) =>
        People.KeyOf(fields.GetValueOrDefault(RoleField)) is { } role ? new ChangeRole(actor, participant, role, Give: true) : null,
        People.NotARole, ct);

    [HttpPost(TakePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public Task<IActionResult> PostTake(CancellationToken ct) => Change((fields, actor, participant) =>
        People.KeyOf(fields.GetValueOrDefault(RoleField)) is { } role ? new ChangeRole(actor, participant, role, Give: false) : null,
        People.NotARole, ct);

    /// <summary>One change: the form's token, the place, the fields; the command <paramref name="command"/> builds from
    /// them, or <paramref name="unsent"/> where what was sent names none; then the writer's outcome.</summary>
    private async Task<IActionResult> Change(Func<IReadOnlyDictionary<string, string>, string, string, ICommand<Participant>?> command,
        string unsent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var participantId = fields.GetValueOrDefault(ParticipantField) ?? "";
        if (command(fields, personId ?? "", participantId) is not { } sent) return await Refused(personId, unsent, ct);
        var outcome = await writer.Send(sent, ct);
        switch (outcome)
        {
            case Outcome<Participant>.Done { Result: var participant }:
                if (sent is Lift) await JoinMemberRole(participant, ct);
                return await Changed(participant, personId, fields, ct);
            case Outcome<Participant>.Refused { Reason: Applications.NoPlace }:
                return PostWrites.Absent();
            case Outcome<Participant>.Refused { Reason: var reason }:
                return await Refused(personId, reason, ct);
            case Outcome<Participant>.Failed { Cause: var cause }:
                throw new InvalidOperationException($"Changing {participantId} failed.", cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for changing {participantId}.");
        }
    }

    /// <summary>The member role for a participant just lifted, after the commit, where they lack it; one this write misses
    /// is joined at their next sign-in.</summary>
    private async Task JoinMemberRole(Participant participant, CancellationToken ct)
    {
        try
        {
            await MemberRole.Join(roles, participant, ct);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(failure, "{Person} was lifted and could not join the member role; their next sign-in joins them", participant.Id);
        }
    }

    /// <summary>A change that committed, or found nothing to change: a form is sent back where it came from; a JSON
    /// request reads the participant as the people now list them for the reader.</summary>
    private async Task<IActionResult> Changed(Participant participant, string? personId, IReadOnlyDictionary<string, string> fields,
        CancellationToken ct)
    {
        if (!PostWrites.WantsJson(Request))
        {
            var back = Back(fields.GetValueOrDefault(BackField));
            Response.Headers.Location = BaseAddress() + back[(baseUrl.PathBase.Value?.Length ?? 0)..];
            return StatusCode(StatusCodes.Status303SeeOther);
        }

        var place = await Capabilities.Load(personId, null, null, ct);
        var lister = await Lister.Load(roles, place, personId, BaseAddress(), ct);
        return new ObjectResult(await lister.Row(participant, ct)) { ContentTypes = { "application/json" } };
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the page again, read afresh,
    /// with the reason above the list.</summary>
    private async Task<IActionResult> Refused(string? personId, string reason, CancellationToken ct)
    {
        var status = Status(reason);
        var gate = !Rule(reason) && status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden;
        if (PostWrites.WantsJson(Request))
        {
            if (gate) return PostWrites.Gated(reason);
            return Rule(reason)
                ? new Problem(status, "Not yours to change", reason, reason).ToResult()
                : new Problem(status, "Not changed", reason).ToResult();
        }

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        var page = await Page(place, personId, null, null, ct);
        if (page.Gate is null) page = page with { Failure = gate ? PostWrites.Sentence(reason) : reason };
        return new ObjectResult(page) { StatusCode = status };
    }

    /// <summary>The status a refusal answers with: <c>404</c> for someone who never arrived, <c>409</c> for a change their
    /// standing does not take, <c>400</c> for what was sent, <c>403</c> for the rule of who changes whom, and a gate's own
    /// otherwise.</summary>
    private static int Status(string reason) => reason switch
    {
        People.NoOne => StatusCodes.Status404NotFound,
        Participant.NotAdmitted or Participant.BannedFirst => StatusCodes.Status409Conflict,
        People.NotADuration or People.NotARole or Participant.TimeoutEnds => StatusCodes.Status400BadRequest,
        _ when Rule(reason) => StatusCodes.Status403Forbidden,
        _ => PostWrites.Status(reason)
    };

    /// <summary>Whether a refusal is the rule of who changes whose standing and roles, rather than a missing verb.</summary>
    private static bool Rule(string reason) => reason is People.Themselves or People.Operator or People.NotBelow or People.NotHeld;

    private async Task<PeoplePage> Page(Capabilities place, string? personId, string? start, People.Cursor? cursor, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var narrowed = start is null ? "" : HandleQuery + "=" + Uri.EscapeDataString(start);
        var here = baseUrl.PathBase + Path + Query(narrowed, cursor is null ? "" : After + "=" + Uri.EscapeDataString(cursor.Seal(protection)));
        var people = baseAddress + Path;
        var gate = ReadGate(place);
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
            Forms(HttpContext, baseAddress, here),
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
    public static string? ReadGate(Capabilities place)
        => place.Holds(Bit.ManageStanding) || place.Holds(Bit.ManageRoles) ? null : place.Gate(Bit.ManageStanding);

    /// <summary>Where each change posts under <paramref name="baseAddress"/>, with a fresh token for
    /// <paramref name="http"/>, returning to <paramref name="back"/>, a path under the base URL.</summary>
    public static PeopleForms Forms(HttpContext http, string baseAddress, string back) => new(
        baseAddress + TimeOutPath, baseAddress + BanPath, baseAddress + LiftPath, baseAddress + GivePath, baseAddress + TakePath,
        People.Durations.Select(duration => duration.Word).ToList(), FormToken.For(http), back);

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

    /// <summary>What one reader may see and do of the people on one page, read once — the place's row, and the roles
    /// given here with what they grant — and then each participant's row from Koan's bag for them, with the names of
    /// their roles read once per role.</summary>
    public sealed class Lister
    {
        private readonly RoleCollection _roles;
        private readonly Capabilities _reader;
        private readonly string? _readerId;
        private readonly IReadOnlyDictionary<string, Role> _given;
        private readonly string _baseAddress;
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

        private Lister(RoleCollection roles, Capabilities reader, string? readerId, TangentSpace space, IReadOnlyDictionary<string, Role> given,
            string baseAddress)
        {
            _roles = roles;
            _reader = reader;
            _readerId = readerId;
            Space = space;
            _given = given;
            _baseAddress = baseAddress;
            foreach (var role in given.Values) _names[role.Key] = role.Name;
        }

        /// <summary>The place's row.</summary>
        public TangentSpace Space { get; }

        public static async Task<Lister> Load(RoleCollection roles, Capabilities reader, string? readerId, string baseAddress, CancellationToken ct)
        {
            var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
            var given = new Dictionary<string, Role>(StringComparer.Ordinal);
            foreach (var (_, key) in People.Given)
            {
                if (await roles.Get(key, ct) is { } role) given[key] = role;
            }

            return new Lister(roles, reader, readerId, space, given, baseAddress);
        }

        /// <summary>One participant as the people list them for the reader.</summary>
        public async Task<PeoplePage.Listed> Row(Participant participant, CancellationToken ct)
        {
            var bag = await _roles.Bag(participant.Id, authenticated: true, ct);
            var held = bag.Roles.Where(role => role is not (RoleTokens.Everyone or RoleTokens.Authenticated)).ToList();
            var names = new List<(int Order, string Name)>(held.Count);
            foreach (var key in held) names.Add((Order(key), await Name(key, ct)));
            var offer = People.Offered(_reader, _readerId, participant, bag.Roles, Space, _given, _now);
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
                names.OrderBy(name => name.Order).ThenBy(name => name.Name, StringComparer.Ordinal).Select(name => name.Name).ToList(),
                offer.Can,
                offer.Give,
                offer.Take);
        }

        /// <summary>A role's name, read once per role; its key where the role is gone.</summary>
        private async Task<string> Name(string key, CancellationToken ct)
        {
            if (_names.TryGetValue(key, out var name)) return name;
            name = (await _roles.Get(key, ct))?.Name ?? key;
            _names[key] = name;
            return name;
        }

        /// <summary>The seeded roles first, highest first; every other role after them.</summary>
        private static int Order(string key) => key switch
        {
            Roles.Administrator => 0,
            Roles.Moderator => 1,
            Roles.Member => 2,
            _ => 3
        };
    }
}
