using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Identity.Management;
using Koan.Identity.Roles;
using Koan.Web.Attributes;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Auth.Extensions;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
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

/// <summary>What choosing one's own kind sends, as a form or one JSON object.</summary>
public sealed record ChooseKindInput([Description(Holds.Kind)] string? Kind = null);

/// <summary>The place's people, a section of its settings at <c>/settings/people</c>, for whoever holds
/// <see cref="Bit.ManageStanding"/> or <see cref="Bit.ManageRoles"/>. Its <c>GET</c> is one bounded page of them
/// (<see cref="People.Read"/>), one resource both ways (<see cref="PeoplePage"/>), continued from the sealed cursor in
/// <c>after</c>, narrowed to the handles that start with <c>handle</c> and to the standing in <c>standing</c>, and, given a
/// role's word in <c>role</c>, read among those who hold it (<see cref="People.Holding"/>) under a cursor of its own; a
/// cursor this server did not seal for the page's order, or a standing that is not one, is refused with <c>400</c>, and a
/// reader who may not read them is refused with the gate, <c>401</c> to sign in and <c>403</c>
/// otherwise: a page reads it with no one listed, and a JSON request as its problem. Its <c>POST</c>s take the participant's id in <c>participant</c>, as a form or a JSON
/// object, and send one command: <c>time-out</c> sends <see cref="TimeOut"/> for the duration in <c>for</c>;
/// <c>ban</c>, <see cref="Ban"/>; <c>lift</c>, <see cref="Lift"/>, after which the participant joins the member role
/// where they lack it (<see cref="MemberRole"/>); <c>give</c> and <c>take</c>, <see cref="ChangeRole"/> for the role in
/// <c>role</c>. A form is sent back to the page in <c>back</c> — this one, the participant's profile, where the same
/// forms are, or the page their card was opened on — and a JSON request reads the participant as this page now lists them; a refusal reads the page again with
/// the reason above the list, or a problem. A reader for whom the place does not exist receives what an address with
/// nothing there receives. Two changes are a participant's own, offered on their own profile: <c>/leave</c> ends the
/// place's grant to write as the account they hold now, sends <see cref="LeavePlace"/> and has Koan erase their person,
/// signing them out, a form sent on to the door; <c>/kind</c> sends <see cref="ChooseKind"/> for the kind in <c>kind</c>,
/// a form sent back to their profile; a JSON request reads where each left them.</summary>
[Managing]
public sealed class PeopleController(KoanWebBaseUrl baseUrl, RoleCollection roles, IDataProtectionProvider protection) : ControllerBase
{
    public const string Path = "/settings/people";

    public const string TimeOutPath = Path + "/time-out";

    public const string BanPath = Path + "/ban";

    public const string LiftPath = Path + "/lift";

    public const string GivePath = Path + "/give";

    public const string TakePath = Path + "/take";

    /// <summary>Where a participant leaves the place.</summary>
    public const string LeavePath = "/leave";

    /// <summary>Where a participant chooses how the place shows them.</summary>
    public const string KindPath = "/kind";

    /// <summary>The field that carries the kind chosen: <c>person</c> or <c>agent</c>.</summary>
    public const string KindField = "kind";

    /// <summary>The refusal of a kind that is neither.</summary>
    public static readonly Reason NotAKind = Reason.Invalid(KindField, "Choose person or agent.");

    /// <summary>What leaving answers as JSON: the door, where whoever left arrives signed out.</summary>
    public sealed record Left(string Door);

    /// <summary>A kind as a choice leaves it: <c>person</c> or <c>agent</c>, whether the account's own bot label holds it
    /// so, and the profile it shows on.</summary>
    public sealed record ChosenKind(string Kind, bool Labelled, string Profile);

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

    /// <summary>The query parameter that narrows the page to a standing: one of <see cref="People.Narrowing"/>.</summary>
    public const string StandingQuery = "standing";

    /// <summary>The query parameter that narrows the page to those who hold a role, by its word.</summary>
    public const string RoleQuery = "role";

    /// <summary>The address of the people who hold the role with <paramref name="word"/>, under <paramref name="baseAddress"/>.</summary>
    public static string Holding(string baseAddress, string word) => baseAddress + Path + "?" + RoleQuery + "=" + Uri.EscapeDataString(word);

    [HttpGet(Path)]
    [Tool(Name = "read_people")]
    [Description("Find the participants admitted here, a page at a time, by the start of a handle, by standing or by a role they hold, with what you may change of each, for whoever holds ManageStanding or ManageRoles. Then call time_out, ban, lift, give_role or take_role where one's entry allows it.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> Get([FromQuery(Name = After), Description(Holds.After)] string? after, [FromQuery(Name = HandleQuery), Description(Holds.HandleStart)] string? handle,
        [FromQuery(Name = StandingQuery), Description(Holds.PeopleStanding)] string? standing, [FromQuery(Name = RoleQuery), Description(Holds.PeopleRole)] string? role,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = Here.Of(HttpContext).Around;
        if (ReadGate(place) is { } refused && PostWrites.WantsJson(Request)) return this.Refused(refused);

        if (!People.TryStanding(standing, out var stands))
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {StandingQuery} parameter is {Wording.Joined(People.Narrowing.Select(People.Word).ToList(), "or")}, or left out for every standing.") { Title = "Not a standing" }.ToResult();
        var narrowed = new Narrowed(People.Start(handle), stands, string.IsNullOrEmpty(role) ? null : role);
        People.Cursor? cursor = null;
        string? members = null;
        if (after is not null && (narrowed.Role is null ? (cursor = People.Cursor.Open(after, protection)) is null : (members = People.Holders.Open(after, protection)) is null))
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {After} parameter is the cursor a page of the people names as its next.") { Title = "Not a page of the people" }.ToResult();

        var page = await Page(place, personId, narrowed, after, cursor, members, ct);
        return ReadGate(place) is { } gate ? new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, gate) } : Ok(page);
    }

    /// <summary>What a page of the people is narrowed to: the start of a handle, a standing and a role's word, each null for
    /// none.</summary>
    private sealed record Narrowed(string? Start, Standing? Standing, string? Role)
    {
        public static readonly Narrowed None = new(null, null, null);

        /// <summary>The narrowing as a query carries it.</summary>
        public string Query() => PeopleController.Query(
            Start is null ? "" : HandleQuery + "=" + Uri.EscapeDataString(Start),
            Standing is { } standing ? StandingQuery + "=" + People.Word(standing) : "",
            Role is null ? "" : RoleQuery + "=" + Uri.EscapeDataString(Role)).TrimStart('?');
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
    [Description("Ban a participant, for whoever holds ManageStanding: the place disappears for them, every page and every stream, as an address with nothing at it answers. lift reverses it.")]
    public Task<IActionResult> PostBan(StandingInput input, CancellationToken ct) => Change(input.Participant, input.Back, (actor, participant) => new Ban(actor, participant), null, ct);

    [HttpPost(LiftPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "lift", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Lift a participant's timeout or ban, so they take part again, for whoever holds ManageStanding.")]
    public Task<IActionResult> PostLift(StandingInput input, CancellationToken ct) => Change(input.Participant, input.Back, (actor, participant) => new Lift(actor, participant), null, ct);

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

    [HttpPost(LeavePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "leave_place", Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description("Leave this Tangent Space for good: you are signed out everywhere, your roles, checkpoints, Connection and asks go, and the place's grant to write as the Bluesky account you hold now ends. Your Posts stay, each under the account you wrote it as, and read as someone who left. Signing in again later arrives anew. An applicant who leaves ends their application. The operator cannot leave.")]
    [Needs(Bit.View, Participant = true, Applicant = true)]
    public async Task<IActionResult> Leave(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User)!;
        if (Here.Of(HttpContext).Rows is { Participant: { } participant } rows)
        {
            if (LeavePlace.Refusal(participant, rows.Space, DateTimeOffset.UtcNow) is { } refused) return this.Refused(refused);
            if (participant.Did.Length > 0) await EndGrant(participant.Did, ct);
        }

        var outcome = await new LeavePlace(personId).Send();
        if (outcome is Outcome<LeavePlace.Gone>.Refused { Reason: var reason }) return this.Refused(reason);
        if (outcome is not Outcome<LeavePlace.Gone>.Done) return Answers.NotDone(HttpContext, outcome);
        if (!await Erased(personId, CancellationToken.None))
            return new Problem(StatusCodes.Status500InternalServerError, "Leaving may not have finished, so send it again to finish it.").ToResult();

        await HttpContext.SignOutAsync(AuthenticationExtensions.CookieScheme);
        var door = BaseAddress() + "/";
        if (PostWrites.WantsJson(Request)) return Ok(new Left(door));
        Response.Headers.Location = door;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    [HttpPost(KindPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "choose_kind", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Choose how this place shows you: person or agent. Nothing behaves differently for an agent; others see the agent tag beside your name. An account that labels itself a bot on Bluesky stays an agent here.")]
    [Needs(Bit.View, Participant = true)]
    public async Task<IActionResult> Choose(ChooseKindInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        ParticipantKind? kind = input.Kind switch { "person" => ParticipantKind.Person, "agent" => ParticipantKind.Agent, _ => null };
        if (kind is not { } chosen) return this.Refused(NotAKind);
        var outcome = await new ChooseKind(PostWrites.PersonId(User) ?? "", chosen).Send();
        if (outcome is Outcome<Participant>.Refused { Reason: var reason }) return this.Refused(reason);
        if (outcome is not Outcome<Participant>.Done { Result: var participant }) return Answers.NotDone(HttpContext, outcome);

        var profile = ProfilePage.AddressOf(BaseAddress(), participant.ShownHandle());
        if (PostWrites.WantsJson(Request))
            return Ok(new ChosenKind(participant.Kind.ToString().ToLowerInvariant(), participant.Labelled, profile));
        Response.Headers.Location = profile;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>Ends the place's grant to write as the account <paramref name="did"/>: revoked at its issuer through Koan's
    /// atproto sessions, outside any hold, before anything of the participant goes, so a leave cut short here leaves them
    /// whole and its resend ends it again. One the place holds no grant for has nothing to end; one whose revocation cannot
    /// be confirmed, a timeout at the issuer among them, is logged, and the participant leaves all the same. Only the
    /// request's own cancellation stops the leave.</summary>
    private async Task EndGrant(string did, CancellationToken ct)
    {
        try
        {
            await HttpContext.RequestServices.GetRequiredService<AtprotoSessions>().Disconnect(did, ct);
        }
        catch (AtprotoAuthorizationRequiredException)
        {
        }
        catch (Exception failed) when (!ct.IsCancellationRequested)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<PeopleController>>()
                .LogWarning(failed, "The grant to write as {Did} could not be ended as its participant left.", did);
        }
    }

    /// <summary>Has Koan erase the person <paramref name="personId"/> (<see cref="IdentityLifecycleService.EraseAsync"/>):
    /// their sessions, links, roles and overwrites go, and their person. Sent once their row is gone, it runs to its end
    /// whatever the request's cancellation, so a closed tab never leaves a person a later sign-in would find again. False
    /// where it did not complete, logged.</summary>
    private async Task<bool> Erased(string personId, CancellationToken ct)
    {
        try
        {
            return (await HttpContext.RequestServices.GetRequiredService<IdentityLifecycleService>().EraseAsync(personId, ct)).Complete;
        }
        catch (InvalidOperationException blocked)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<PeopleController>>().LogError(blocked, "Erasing {Person} as they left was blocked.", personId);
            return false;
        }
    }

    /// <summary>One change: the place; the command <paramref name="command"/> builds for who sent it and
    /// whom it names, or <paramref name="unsent"/> where what was sent names none; then the command's outcome.</summary>
    private async Task<IActionResult> Change(string? named, string? back, Func<string, string, ICommand<Participant>?> command, Reason? unsent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);

        if (command(personId ?? "", named ?? "") is not { } sent) return await Refused(personId, unsent!, ct);
        var outcome = await sent.Send();
        switch (outcome)
        {
            case Outcome<Participant>.Done { Result: var participant }:
                return await Changed(participant, personId, back, ct);
            case Outcome<Participant>.Refused { Reason: var reason }:
                return await Refused(personId, reason, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
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
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);
        var page = await Page(place, personId, Narrowed.None, null, null, null, ct);
        if (page.Gate is null) page = page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    /// <summary>One page of the people as <paramref name="narrowed"/> says, continued from <paramref name="after"/> as it was
    /// sent, which opened as <paramref name="cursor"/> in handle order or as Koan's <paramref name="members"/> among a role's
    /// members: those who hold the role a word names, where it names one, and no one where it names none here.</summary>
    private async Task<PeoplePage> Page(Capabilities place, string? personId, Narrowed narrowed, string? after, People.Cursor? cursor, string? members,
        CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var query = narrowed.Query();
        var here = baseUrl.PathBase + Path + Query(query, after is null ? "" : After + "=" + Uri.EscapeDataString(after));
        var people = baseAddress + Path;
        var gate = ReadGate(place)?.Word;
        var lister = await Lister.Load(roles, place, personId, baseAddress, ct);
        var held = narrowed.Role is { } word && Roles.KeyOf(word) is { } key ? await lister.RoleOf(key, ct) : null;
        IReadOnlyList<Participant> read = [];
        string? next = null;
        if (gate is null && held is not null)
        {
            var holders = await People.Holding(roles, held.Key, narrowed.Start, narrowed.Standing, members, ct);
            (read, next) = (holders.Participants, holders.Next is { } more ? People.Holders.Seal(more, protection) : null);
        }
        else if (gate is null && narrowed.Role is null)
        {
            var page = await People.Read(narrowed.Start, cursor, ct, narrowed.Standing);
            (read, next) = (page.Participants, page.Next?.Seal(protection));
        }

        var listed = new List<PeoplePage.Listed>(read.Count);
        foreach (var participant in read) listed.Add(await lister.Row(participant, ct));
        return new PeoplePage(
            TangentSpaceSettingsController.Sections(place, baseAddress, TangentSpaceSettingsController.PeopleSection),
            listed,
            next is null ? null : people + Query(query, After + "=" + Uri.EscapeDataString(next)),
            after is not null,
            people + Query(query, ""),
            narrowed.Start ?? "",
            narrowed.Standing is { } standing ? People.Word(standing) : "",
            narrowed.Role ?? "",
            held?.Name,
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
            standing ? Changing(TimeOutPath, "time-out in their can", new Form.Field(ForField, "How long the timeout lasts: " + Wording.Joined(durations, "or") + ".")) : null,
            standing ? Changing(BanPath, "ban in their can") : null,
            standing ? Changing(LiftPath, "lift in their can") : null,
            roles ? Changing(GivePath, "the role in their give", new Form.Field(RoleField, Holds.GivenRole)) : null,
            roles ? Changing(TakePath, "the role in their take", new Form.Field(RoleField, Holds.TakenRole)) : null,
            durations);
    }

    /// <summary>The page a form returns to: the path it sent in <c>back</c> where that is under the base URL's, of visible
    /// characters, without scheme or authority — the people, a profile, or the page a person's card was opened on; the
    /// people otherwise.</summary>
    private string Back(string? sent)
    {
        var people = baseUrl.PathBase + Path;
        if (sent is not { Length: > 0 } || sent[0] != '/' || sent.Length > 1 && sent[1] is '/' or '\\'
            || !sent.All(c => c is > ' ' and <= '~' and not '\\'))
            return people;
        return baseUrl.PathBase.HasValue && !new PathString(sent.Split('?', '#')[0]).StartsWithSegments(baseUrl.PathBase) ? people : sent;
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
                var role = await RoleOf(key, ct);
                names.Add((role?.Position ?? 0, role?.Name ?? key));
            }

            var offer = People.Offered(_reader, _readerId, participant, bag, Space, _given, _now);
            var standing = participant.StandingAt(_now);
            var handle = participant.ShownHandle();
            return new PeoplePage.Listed(
                new Render.Person(participant.Id, handle, participant.ShownName(), participant.Kind.ToString().ToLowerInvariant(), participant.Profile.Avatar,
                    ProfilePage.AddressOf(_baseAddress, handle)),
                People.Word(standing),
                standing == Standing.TimedOut ? participant.StandingUntil : null,
                names.OrderByDescending(name => name.Position).ThenBy(name => name.Name, StringComparer.Ordinal).Select(name => name.Name).ToList(),
                offer.Can,
                offer.Give,
                offer.Take);
        }

        /// <summary>A role's row, read once per role; null where the role is gone.</summary>
        public async Task<Role?> RoleOf(string key, CancellationToken ct)
        {
            if (_read.TryGetValue(key, out var role)) return role;
            role = await _roles.Get(key, ct);
            _read[key] = role;
            return role;
        }
    }
}
