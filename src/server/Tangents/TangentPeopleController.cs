using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>What letting a participant in or out sends, as a form or one JSON object.</summary>
public sealed record LetInInput(
    [Description(Holds.Member)] string? Person = null,
    [Description(Holds.Letting)] string? Choice = null);

/// <summary>What setting a Tangent's joining sends, as a form or one JSON object.</summary>
public sealed record JoiningInput([Description(Holds.Joining)] string? Joining = null);

/// <summary>A Tangent's people at <c>/t/{tangent}/settings/people</c>, for whoever holds <see cref="Bit.ManageTangent"/>
/// there (<see cref="TangentPeoplePage"/>). Its <c>GET</c> is one page of who is in by role and by person, from Koan's
/// overwrites on the Tangent's resource, continued by <c>after</c>, with the asks that wait and, by <c>find</c>, the
/// participants whose handles start with it. Its <c>POST</c> takes <c>person</c> and <c>choice</c> and sends
/// <see cref="LetIn"/>; <c>POST /t/{tangent}/settings/joining</c> takes <c>joining</c> and sends
/// <see cref="ChangeJoining"/>. A form is sent back to the people; a JSON request reads the page as it now
/// stands.</summary>
[Managing]
public sealed class TangentPeopleController(KoanWebBaseUrl baseUrl, RoleCollection roles) : ControllerBase
{
    public const string Segment = "people";
    public const string JoiningSegment = "joining";
    public const string Route = TangentSettingsController.Route + "/" + Segment;
    public const string JoiningRoute = TangentSettingsController.Route + "/" + JoiningSegment;
    public const string FindQuery = "find";
    public const string AfterQuery = "after";
    public const string PersonField = "person";
    public const string ChoiceField = "choice";
    public const string JoiningField = "joining";

    /// <summary>Where a person stands whose own overwrite keeps them out.</summary>
    public const string KeptOutState = "kept-out";

    /// <summary>Where a person stands who reads the Tangent through a role, with no overwrite of their own.</summary>
    public const string RoleState = "by-role";

    /// <summary>The most people one page lists, and the most a search finds.</summary>
    public const int PageSize = 25;

    public static readonly Reason ChoiceRule = Reason.Invalid("Let a participant in or out: send in or out.");
    public static readonly Reason JoiningRule = Reason.Invalid("A Tangent's joining is closed, join or ask.");

    /// <summary>How someone outside a Tangent comes in, as words.</summary>
    public static readonly IReadOnlyList<string> Joinings = Enum.GetValues<Joining>().Select(Word).ToList();

    public static string Word(Joining joining) => joining.ToString().ToLowerInvariant();

    [HttpGet(Route)]
    [Needs(Bit.ManageTangent)]
    [Tool(Name = "read_tangent_people")]
    [Description("Read who reads and posts in a Tangent you manage, by role and by person, a page at a time, the asks to join that wait, and, with find, the participants whose handles start with it. Follow with let_into_tangent or change_tangent_joining.")]
    public async Task<IActionResult> Get([Description(Holds.Tangent)] string tangent, [FromQuery(Name = FindQuery), Description(Holds.Find)] string? find,
        [FromQuery(Name = AfterQuery), Description(Holds.After)] string? after, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await Page(Here.Of(HttpContext), find, after, ct));
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Needs(Bit.ManageTangent)]
    [Tool(Name = "let_into_tangent", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Let a participant into a private Tangent you manage, or out: out declines their waiting ask, or takes them out. Their door tells them when you decide an ask.")]
    public async Task<IActionResult> Let([FromRoute, Description(Holds.Tangent)] string tangent, LetInInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        if (input.Choice is not ("in" or "out")) return await Refused(found, ChoiceRule, ct);
        var outcome = await new LetIn(found.Tangent!.Id, found.PersonId!, input.Person ?? "", input.Choice == "in").Send();
        return await Answered(found, outcome, ct);
    }

    [HttpPost(JoiningRoute)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Needs(Bit.ManageTangent)]
    [Tool(Name = "change_tangent_joining", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Set how someone outside a private Tangent you manage comes in: closed, join or ask.")]
    public async Task<IActionResult> SetJoining([FromRoute, Description(Holds.Tangent)] string tangent, JoiningInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        if (TangentWrites.Choice(input.Joining, Joinings, out _) is not { } chosen) return await Refused(found, JoiningRule, ct);
        var outcome = await new ChangeJoining(found.Tangent!.Id, found.PersonId!, Enum.GetValues<Joining>()[chosen]).Send();
        return await Answered(found, outcome, ct);
    }

    private async Task<IActionResult> Answered(Here found, Outcome<Tangent> outcome, CancellationToken ct)
    {
        switch (outcome)
        {
            case Outcome<Tangent>.Done:
                if (!PostWrites.WantsJson(Request))
                {
                    Response.Headers.Location = PeopleAddress(BaseAddress(), found.Tangent!.Id);
                    return StatusCode(StatusCodes.Status303SeeOther);
                }

                var (again, gone) = await Here.Find(found.PersonId, found.Tangent!.Id, null, null, ct);
                return gone is not null ? this.Refused(gone) : Ok(await Page(again, null, null, ct));
            case Outcome<Tangent>.Refused { Reason: var reason } when reason != ChangeTangent.NoTangent:
                return await Refused(found, reason, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the page again with the
    /// reason above it.</summary>
    private async Task<IActionResult> Refused(Here found, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
        var page = await Page(found, null, null, ct) with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    public static string PeopleAddress(string baseAddress, string tangentId)
        => PostWrites.TangentAddress(baseAddress, tangentId) + "/" + TangentWrites.SettingsSegment + "/" + Segment;

    private async Task<TangentPeoplePage> Page(Here found, string? find, string? after, CancellationToken ct)
    {
        var row = found.Tangent!;
        var baseAddress = BaseAddress();
        var address = PostWrites.TangentAddress(baseAddress, row.Id);
        var settings = address + "/" + TangentWrites.SettingsSegment;
        var people = settings + "/" + Segment;
        var space = found.Rows.Space;
        var isFixed = space.ModeratorsTangentId == row.Id;

        var read = await roles.Overwrites(Overwrites.Resource(row.Id), after, PageSize, ct);
        var roleRows = new List<TangentPeoplePage.RoleHere>();
        foreach (var overwrite in read.Items.Where(o => o.SubjectKind == OverwriteSubjectKind.Role && o.SubjectId != Roles.Everyone))
            if (Overwrites.AccessOf(overwrite) is not RoleAccess.Default and var access && await roles.Get(overwrite.SubjectId, ct) is { } role)
                roleRows.Add(new TangentPeoplePage.RoleHere(Roles.Word(role.Key), role.Name, TangentAccessController.Word(access)));
        var personal = read.Items.Where(o => o.SubjectKind == OverwriteSubjectKind.Person && (Inside.In(o) || Inside.Out(o))).ToList();
        var inIds = personal.Select(o => o.SubjectId).ToList();

        var asks = await Ask.Waiting(row.Id, Ask.ReadSize, ct);
        var start = Participants.People.Start(find);
        var matched = start is null ? [] : (await Participants.People.Read(start, null, ct)).Participants;
        var named = await Named(inIds.Concat(asks.Select(ask => ask.PersonId)).Distinct(StringComparer.Ordinal).ToList(), ct);
        foreach (var participant in matched) named[participant.Id] = participant;

        IReadOnlyList<TangentPeoplePage.Person>? foundPeople = null;
        if (start is not null)
        {
            var own = await PermissionOverwrite.Get(matched.Select(p =>
                PermissionOverwrite.KeyFor(Overwrites.Resource(row.Id), OverwriteSubjectKind.Person, p.Id)).ToList(), ct);
            var asked = await Ask.Get(matched.Select(p => Ask.Key(row.Id, p.Id)).ToList(), ct);
            // Each found participant by what they can do here: kept out, in by their own place, reading through a role, or
            // asked, declined or out.
            var states = new List<string>(matched.Count);
            for (var index = 0; index < matched.Count; index++)
                states.Add(Inside.Out(own[index]) ? KeptOutState
                    : Inside.In(own[index]) ? Joined.InState
                    : (await Capabilities.Load(matched[index].Id, row.Id, null, ct)).Exists ? RoleState
                    : asked[index] is { } ask ? ask.Declined ? Joined.DeclinedState : Joined.AskedState
                    : "out");
            foundPeople = matched.Select((participant, index) => Person(participant, baseAddress, states[index])).ToList();
        }

        return new TangentPeoplePage(
            row.Id,
            row.Name,
            address,
            settings,
            settings + "/" + TangentAccessController.Segment,
            TangentWrites.Word(await Overwrites.AudienceOf(row.Id, ct) ?? Audience.Public),
            isFixed,
            Word(row.Joining),
            Joinings,
            isFixed ? null : Form.Posting(settings + "/" + JoiningSegment, new Form.Field(JoiningField, Holds.Joining, Word(row.Joining))),
            asks.Where(ask => named.ContainsKey(ask.PersonId)).Select(ask => Person(named[ask.PersonId], baseAddress, Joined.AskedState)).ToList(),
            roleRows,
            personal.Where(o => named.ContainsKey(o.SubjectId))
                .Select(o => Person(named[o.SubjectId], baseAddress, Inside.Out(o) ? KeptOutState : Joined.InState)).ToList(),
            read.Next is { } next ? people + "?" + AfterQuery + "=" + Uri.EscapeDataString(next) : null,
            start,
            foundPeople,
            isFixed ? null : Form.Posting(people, new Form.Field(PersonField, Holds.Member), new Form.Field(ChoiceField, Holds.Letting)),
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(found.PersonId, found.Capabilities, baseAddress, baseUrl.PathBase + "/t/" + row.Id + "/" + TangentWrites.SettingsSegment + "/" + Segment, ct));
    }

    private static async Task<Dictionary<string, Participant>> Named(IReadOnlyList<string> ids, CancellationToken ct)
        => ids.Count == 0
            ? new Dictionary<string, Participant>(StringComparer.Ordinal)
            : (await Participant.Get(ids, ct)).OfType<Participant>().ToDictionary(p => p.Id, StringComparer.Ordinal);

    private static TangentPeoplePage.Person Person(Participant participant, string baseAddress, string state)
    {
        var handle = participant.Handle.Length > 0 ? participant.Handle : participant.Id;
        var name = participant.Profile.DisplayName is { Length: > 0 } shown ? shown : handle;
        return new TangentPeoplePage.Person(participant.Id, name, handle, participant.Kind.ToString().ToLowerInvariant(),
            ProfilePage.AddressOf(baseAddress, handle), state);
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
