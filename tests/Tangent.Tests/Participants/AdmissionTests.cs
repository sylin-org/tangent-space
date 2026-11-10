using System.Net;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Participants;

/// <summary>Admission: on a place whose admission says join, whoever signs in admitted is a member and talks at once, and
/// every sign-in joins an admitted participant who lacks the role, never a banned one or an applicant; the place's
/// admission is set at <c>/settings/admission</c>, appending space.changed; on a place that says apply, a newcomer
/// applies, reads, posts nothing, and their door says their application is waiting; whoever holds ManageStanding admits
/// and declines them at <c>/settings/applications</c>, each decision a standing.changed that targets the applicant, whose
/// door then tells them; anyone else sees no applications and is refused; and every answer carries <c>can</c> and
/// <c>gate</c>.</summary>
public sealed class AdmissionTests
{
    private const string General = "general";
    private static readonly string Grace = Persona.Id("grace4admission");
    private static readonly string Hal = Persona.Id("hal4admission");
    private static readonly string Ivy = Persona.Id("ivy4admission");

    private static async Task<bool> IsMember(TangentSpaceFixture tangentSpace, string id)
        => (await tangentSpace.Services.GetRequiredService<RoleCollection>().Bag(id, authenticated: true, Cancellation)).InRole(Roles.Member);

    private static async Task<IReadOnlyList<string?>> Can(Browser browser, string path)
        => (await Send(browser.Client, HttpMethod.Get, path, json: true)).Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()).ToList();

    /// <summary>Starts a Topic in <paramref name="tangent"/> as JSON, with the session's token from the Tangent's page, which
    /// carries a form for every signed-in reader, so an applicant the new-topic page offers no form still sends one.</summary>
    private static Task<Answered> StartIn(Browser browser, string tangent, string title)
        => Json(browser, "/t/" + tangent + "/new", "/t/" + tangent, new { id = Tids.Mint(), title, body = "Hello, everyone." });

    [Fact]
    public async Task On_a_join_place_a_newcomer_who_signs_in_is_a_member_and_starts_a_topic_in_general()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        // First run, as whoever signs in first sets the place up: General is there from its first minute.
        using var leo = new Browser(tangentSpace);
        (await leo.SignIn("leo", Persona.Id("leo4admission"), Cancellation, handle: "leo.example")).Dispose();
        Assert.Equal(Admission.Join, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Admission);

        using var grace = await SignedIn(tangentSpace, Grace);

        Assert.True(await IsMember(tangentSpace, Grace));
        Assert.Equal(Standing.Admitted, (await Participant.Get(Grace, Cancellation))!.Standing);
        Assert.Contains(nameof(Bit.Topic), await Can(grace, "/t/" + General));
        var started = await StartIn(grace, General, "First words");
        Assert.Equal(HttpStatusCode.Created, started.Status);
        Assert.Equal((General, Grace), (started.Json.GetProperty("tangent").GetString(), started.Json.GetProperty("authorId").GetString()));
        Assert.False((await Send(grace.Client, HttpMethod.Get, "/", json: true)).Json.TryGetProperty("gate", out _));
    }

    [Fact]
    public async Task An_admitted_participant_without_the_member_role_joins_it_at_their_next_sign_in_and_a_banned_one_or_an_applicant_never_does()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.ATangent(tangentSpace, General, ct: Cancellation);
        // Arrived before joining made anyone a member.
        await Given.AParticipant(tangentSpace, Grace, [], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Hal, [], Standing.Banned, Cancellation);
        await Given.AParticipant(tangentSpace, Ivy, [], Standing.Applied, Cancellation);
        Assert.False(await IsMember(tangentSpace, Grace));

        using var grace = await SignedIn(tangentSpace, Grace);
        using var hal = await SignedIn(tangentSpace, Hal);
        using var ivy = await SignedIn(tangentSpace, Ivy);

        Assert.True(await IsMember(tangentSpace, Grace));
        Assert.Contains(nameof(Bit.Post), await Can(grace, "/t/" + General));
        Assert.False(await IsMember(tangentSpace, Hal));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(hal.Client, HttpMethod.Get, "/t/" + General, json: true)).Status);
        Assert.False(await IsMember(tangentSpace, Ivy));
        Assert.Equal([nameof(Bit.View)], await Can(ivy, "/t/" + General));

        // A second sign-in finds the role in place and writes nothing more.
        using var again = await SignedIn(tangentSpace, Grace);
        Assert.Single((await tangentSpace.Services.GetRequiredService<RoleCollection>().Members(Roles.Member, ct: Cancellation)).Items,
            member => member.PersonId == Grace);
    }

    [Fact]
    public async Task Whoever_holds_manage_space_switches_the_place_to_apply_at_settings_admission_which_appends_space_changed_and_anyone_else_is_refused()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var leoId = Persona.Id("leo4admission");
        await Given.AParticipant(tangentSpace, leoId, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, leoId);
        using var ada = await SignedIn(tangentSpace, Ada);
        var path = TangentSpaceSettingsController.AdmissionPath;

        var page = await Send(leo.Client, HttpMethod.Get, path, json: true);
        Assert.Equal((HttpStatusCode.OK, "join"), (page.Status, page.Json.GetProperty("admission").GetString()));
        Assert.Equal(["join", "apply"], page.Json.GetProperty("admissions").EnumerateArray().Select(word => word.GetString()));
        Assert.Equal(["General", "Admission", "Applications", "Appearance", "People", "Roles", "Export"], page.Json.GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("name").GetString()));
        Assert.False(page.Json.TryGetProperty("gate", out _));
        var html = await Send(leo.Client, HttpMethod.Get, path);
        Assert.Contains("Join at once", html.Body);
        Assert.Contains("Apply and wait", html.Body);
        var before = await Event.Count.Exact(Cancellation);

        var saved = await Form(leo, path, path, new Dictionary<string, string> { [TangentSpaceSettingsController.AdmissionField] = "apply" });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + path), (saved.Status, saved.Location));
        Assert.Equal(Admission.Apply, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Admission);
        var logged = Assert.Single(await Event.All(Cancellation), e => e.Seq > before);
        Assert.Equal((EventKind.SpaceChanged, leoId), (logged.Kind, logged.ActorId));
        Assert.Contains("value=\"apply\" checked", (await Send(leo.Client, HttpMethod.Get, path)).Body);

        // As JSON, back to join; a word that is neither is refused, and the same choice writes nothing.
        var joined = await Json(leo, path, path, new { admission = "join" });
        Assert.Equal((HttpStatusCode.OK, "join"), (joined.Status, joined.Json.GetProperty("admission").GetString()));
        Assert.Equal(before + 2, await Event.Count.Exact(Cancellation));
        var unknown = await Json(leo, path, path, new { admission = "invite" });
        Assert.Equal((HttpStatusCode.BadRequest, "invalid"), (unknown.Status, unknown.Word));
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, path, path, new { admission = "join" })).Status);
        Assert.Equal(before + 2, await Event.Count.Exact(Cancellation));

        // A member reads the choice and changes nothing, and their rail lists nothing, for they may use none of it.
        var read = await Send(ada.Client, HttpMethod.Get, path, json: true);
        Assert.Equal((HttpStatusCode.OK, Capabilities.NoRoleGrants(Bit.ManageSpace).Word), (read.Status, read.Json.GetProperty("gate").GetString()));
        Assert.Equal(0, read.Json.GetProperty("sections").GetArrayLength());
        var refused = await Json(ada, "/t/" + Workshop, path, new { admission = "apply" });
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageSpace).Word), (refused.Status, refused.Word));
        Assert.Equal(Admission.Join, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Admission);
        Assert.Equal(before + 2, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task On_an_apply_place_a_newcomer_applies_reads_posts_nothing_and_their_door_says_their_application_is_waiting()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, admission: Admission.Apply, ct: Cancellation);
        await Given.ATangent(tangentSpace, General, ct: Cancellation);

        using var grace = await SignedIn(tangentSpace, Grace);

        var row = (await Participant.Get(Grace, Cancellation))!;
        Assert.Equal(Standing.Applied, row.Standing);
        Assert.NotNull(row.AppliedAt);
        Assert.False(await IsMember(tangentSpace, Grace));
        Assert.Equal([nameof(Bit.View)], await Can(grace, "/t/" + General));
        var started = await StartIn(grace, General, "Hello?");
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.Apply.Word), (started.Status, started.Word));
        Assert.Equal(0, await Post.Count.Exact(Cancellation));

        var door = await Send(grace.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal(Capabilities.Apply.Word, door.Json.GetProperty("gate").GetString());
        Assert.Equal([nameof(Bit.View)], door.Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));
        var html = await Send(grace.Client, HttpMethod.Get, "/");
        Assert.Contains("Your application is waiting", html.Body);
        Assert.Contains($"data-gate=\"{Capabilities.Apply.Word}\"", html.Body);
    }

    [Fact]
    public async Task A_holder_of_manage_standing_admits_one_applicant_who_then_posts_and_declines_another_each_a_targeted_event_and_anyone_else_is_refused()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.ATangent(tangentSpace, General, ct: Cancellation);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Admission = Admission.Apply;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
        using var grace = await SignedIn(tangentSpace, Grace);
        using var hal = await SignedIn(tangentSpace, Hal);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var ada = await SignedIn(tangentSpace, Ada);
        var path = ApplicationsController.Path;

        // The moderator holds ManageStanding: the account menu opens the applications that wait, newest first, every
        // application one press from them.
        Assert.Equal(ApplicationsController.WaitingAddress(BaseUrl), (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("who").GetProperty("settings").GetString());
        var opened = await Send(mo.Client, HttpMethod.Get, ApplicationsController.WaitingAddress(""));
        Assert.Contains(">Every application<", opened.Body);
        Assert.Contains("href=\"" + BaseUrl + "/p/grace4admission.example\"", opened.Body);
        var listed = await Send(mo.Client, HttpMethod.Get, path, json: true);
        Assert.Equal(HttpStatusCode.OK, listed.Status);
        Assert.False(listed.Json.TryGetProperty("gate", out _));
        Assert.Contains(nameof(Bit.ManageStanding), listed.Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));
        Assert.Equal([(Hal, "hal4admission.example", "waiting"), (Grace, "grace4admission.example", "waiting")],
            listed.Json.GetProperty("applications").EnumerateArray()
                .Select(applicant => (applicant.GetProperty("id").GetString(), applicant.GetProperty("handle").GetString(), applicant.GetProperty("state").GetString())));
        Assert.Equal(BaseUrl + "/p/grace4admission.example", listed.Json.GetProperty("applications")[1].GetProperty("profile").GetString());
        var html = await Send(mo.Client, HttpMethod.Get, path);
        Assert.Contains("href=\"" + BaseUrl + "/p/grace4admission.example\"", html.Body);
        Assert.Contains("<summary class=\"button\">Decline</summary>", html.Body);
        var before = await Event.Count.Exact(Cancellation);

        var admitted = await Form(mo, path, ApplicationsController.AdmitPath, new Dictionary<string, string> { [ApplicationsController.ParticipantField] = Grace });
        var declined = await Json(mo, path, ApplicationsController.DeclinePath, new { participant = Hal });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + path), (admitted.Status, admitted.Location));
        Assert.Equal((HttpStatusCode.OK, "declined"), (declined.Status, declined.Json.GetProperty("standing").GetString()));
        Assert.Contains(nameof(Bit.ManageStanding), declined.Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));
        var graceRow = (await Participant.Get(Grace, Cancellation))!;
        Assert.Equal((Standing.Admitted, (DateTimeOffset?)null), (graceRow.Standing, graceRow.AppliedAt));
        Assert.True(await IsMember(tangentSpace, Grace));
        Assert.Equal(Standing.Declined, (await Participant.Get(Hal, Cancellation))!.Standing);
        Assert.False(await IsMember(tangentSpace, Hal));
        var logged = (await Event.All(Cancellation)).Where(e => e.Seq > before).OrderBy(e => e.Seq).ToList();
        Assert.Equal([(EventKind.StandingChanged, Mo, Grace, Standing.Admitted), (EventKind.StandingChanged, Mo, Hal, Standing.Declined)],
            logged.Select(e => (e.Kind, e.ActorId, e.TargetId, e.Standing)));

        // The admitted applicant's door tells them, and they post; the declined one's says so, and they post nothing.
        var graceDoor = await Send(grace.Client, HttpMethod.Get, "/", json: true);
        Assert.False(graceDoor.Json.TryGetProperty("gate", out _));
        Assert.Equal(Mo, graceDoor.Json.GetProperty("news").GetProperty("admitted").GetProperty("who").GetProperty("id").GetString());
        Assert.Contains("admitted you: you can start topics and reply here.", (await Send(grace.Client, HttpMethod.Get, "/")).Body);
        Assert.Equal(HttpStatusCode.Created, (await StartIn(grace, General, "Glad to be here")).Status);
        var halDoor = await Send(hal.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal(Capabilities.Declined.Word, halDoor.Json.GetProperty("gate").GetString());
        Assert.Contains("Your application was declined", (await Send(hal.Client, HttpMethod.Get, "/")).Body);
        var halStarted = await StartIn(hal, General, "Still here");
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.Declined.Word), (halStarted.Status, halStarted.Word));

        // The declined application stays listed, and a later admission reverses it.
        var after = await Send(mo.Client, HttpMethod.Get, path, json: true);
        Assert.Equal([(Hal, "declined")], after.Json.GetProperty("applications").EnumerateArray()
            .Select(applicant => (applicant.GetProperty("id").GetString(), applicant.GetProperty("state").GetString())));
        Assert.Equal(HttpStatusCode.OK, (await Json(mo, path, ApplicationsController.AdmitPath, new { participant = Hal })).Status);
        Assert.True(await IsMember(tangentSpace, Hal));
        // Deciding an admitted participant again: admitting writes nothing, and declining is refused.
        using var ivy = await SignedIn(tangentSpace, Ivy);
        var events = await Event.Count.Exact(Cancellation);
        // No application is listed now, so the token comes from the page of a Tangent, which carries a form.
        var settings = "/t/" + General;
        Assert.Equal(HttpStatusCode.OK, (await Json(mo, settings, ApplicationsController.AdmitPath, new { participant = Hal })).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Json(mo, settings, ApplicationsController.DeclinePath, new { participant = Grace })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Json(mo, settings, ApplicationsController.AdmitPath, new { participant = Persona.Id("nobody") })).Status);
        Assert.Equal(events, await Event.Count.Exact(Cancellation));

        // A member sees no applications, in the rail or the page, and is refused.
        var refusedPage = await Send(ada.Client, HttpMethod.Get, path, json: true);
        var gate = Capabilities.NoRoleGrants(Bit.ManageStanding).Word;
        Assert.Equal((HttpStatusCode.Forbidden, gate), (refusedPage.Status, refusedPage.Json.GetProperty("word").GetString()));
        Assert.False(refusedPage.Json.TryGetProperty("applications", out _));
        Assert.DoesNotContain("ivy4admission", (await Send(ada.Client, HttpMethod.Get, path)).Body);
        Assert.DoesNotContain("Applications", (await Send(ada.Client, HttpMethod.Get, TangentSpaceSettingsController.Path, json: true)).Json
            .GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("name").GetString()));
        var refused = await Json(ada, "/t/" + General, ApplicationsController.AdmitPath, new { participant = Ivy });
        Assert.Equal((HttpStatusCode.Forbidden, gate), (refused.Status, refused.Word));
        Assert.Equal(Standing.Applied, (await Participant.Get(Ivy, Cancellation))!.Standing);
        using var visitor = Agent(tangentSpace);
        var visiting = await Send(visitor, HttpMethod.Get, path, json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word), (visiting.Status, visiting.Json.GetProperty("word").GetString()));
        Assert.Equal(events, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task The_applications_are_read_a_bounded_page_at_a_time_newest_first_through_a_cursor()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var start = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var count = Applications.PageSize + 2;
        for (var i = 0; i < count; i++)
        {
            var applicant = Participant.Arrive(Persona.Id($"applicant{i:D2}"), Persona.Did(Persona.Id($"applicant{i:D2}")), $"applicant{i:D2}.example", Admission.Apply, start.AddMinutes(i));
            Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(applicant).Send(Cancellation));
        }

        using var mo = await SignedIn(tangentSpace, Mo);
        var first = await Send(mo.Client, HttpMethod.Get, ApplicationsController.Path, json: true);
        var firstIds = first.Json.GetProperty("applications").EnumerateArray().Select(applicant => applicant.GetProperty("id").GetString()).ToList();
        Assert.Equal(Applications.PageSize, firstIds.Count);
        Assert.Equal(Persona.Id($"applicant{count - 1:D2}"), firstIds[0]);
        var next = first.Json.GetProperty("next").GetString()!;
        Assert.False(first.Json.GetProperty("continued").GetBoolean());

        var second = await Send(mo.Client, HttpMethod.Get, new Uri(next).PathAndQuery, json: true);
        Assert.Equal([Persona.Id("applicant01"), Persona.Id("applicant00")],
            second.Json.GetProperty("applications").EnumerateArray().Select(applicant => applicant.GetProperty("id").GetString()));
        Assert.True(second.Json.GetProperty("continued").GetBoolean());
        Assert.False(second.Json.TryGetProperty("next", out var none) && none.ValueKind != System.Text.Json.JsonValueKind.Null);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(mo.Client, HttpMethod.Get, ApplicationsController.Path + "?after=nonsense", json: true)).Status);
    }

    [Fact]
    public async Task The_applications_continue_from_one_sealed_cursor_on_both_doors_whoever_applies_meanwhile()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var start = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        async Task Applies(string name, DateTimeOffset at)
        {
            var applicant = Participant.Arrive(Persona.Id(name), Persona.Did(Persona.Id(name)), name + ".example", Admission.Apply, at);
            Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(applicant).Send(Cancellation));
        }
        for (var i = 0; i < Applications.PageSize + 2; i++) await Applies($"seek{i:D2}", start.AddMinutes(i));

        using var mo = await SignedIn(tangentSpace, Mo);
        var first = (await Send(mo.Client, HttpMethod.Get, ApplicationsController.Path, json: true)).Json;
        var firstIds = first.GetProperty("applications").EnumerateArray().Select(applicant => applicant.GetProperty("id").GetString()).ToList();
        var next = new Uri(first.GetProperty("next").GetString()!);
        // The cursor is sealed: it names no applicant, and one altered does not open.
        Assert.DoesNotContain("seek", next.Query);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(mo.Client, HttpMethod.Get, next.PathAndQuery + "x", json: true)).Status);

        // Someone applies between the two pages: the newest leads the first page read again, and the second page goes on
        // from where the first ended, repeating no one and missing no one.
        await Applies("seeklate", start.AddHours(1));
        var second = (await Send(mo.Client, HttpMethod.Get, next.PathAndQuery, json: true)).Json;
        Assert.Equal([Persona.Id("seek01"), Persona.Id("seek00")],
            second.GetProperty("applications").EnumerateArray().Select(applicant => applicant.GetProperty("id").GetString()));
        Assert.Empty(firstIds.Intersect(second.GetProperty("applications").EnumerateArray().Select(applicant => applicant.GetProperty("id").GetString())));

        // An agent reads the same pages, continuing from the cursor the page names, and the list reads its rows alone.
        using var tools = await Tools.ToolClient.Connect(tangentSpace, mo);
        var read = await tools.Call("read_applications", new { });
        Assert.Equal(Persona.Id("seeklate"), read.Structured!.Value.GetProperty("applications")[0].GetProperty("id").GetString());
        var after = QueryHelpers.ParseQuery(new Uri(read.Structured.Value.GetProperty("next").GetString()!).Query)[ApplicationsController.After].ToString();
        var older = await tools.Call("read_applications", new { after });
        Assert.Equal([Persona.Id("seek02"), Persona.Id("seek01"), Persona.Id("seek00")],
            older.Structured!.Value.GetProperty("applications").EnumerateArray().Select(applicant => applicant.GetProperty("id").GetString()));
        Assert.Equal(400, (await tools.Call("read_applications", new { state = "declined" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(mo.Client, HttpMethod.Get, ApplicationsController.Path + "?set=audit", json: true)).Status);
        // The list pages by its cursor alone, so a page number is ignored and the first page answers.
        var numbered = await Send(mo.Client, HttpMethod.Get, ApplicationsController.Path + "?page=2", json: true);
        Assert.Equal(HttpStatusCode.OK, numbered.Status);
        Assert.Equal(Persona.Id("seeklate"), numbered.Json.GetProperty("applications")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task The_applications_that_wait_are_read_alone_newest_first_a_page_at_a_time_and_a_decision_there_comes_back_to_them()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var start = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var count = Applications.PageSize + 2;
        // The waiting applications, then three newer ones that were declined.
        for (var i = 0; i < count + 3; i++)
        {
            var applicant = Participant.Arrive(Persona.Id($"waiting{i:D2}"), Persona.Did(Persona.Id($"waiting{i:D2}")), $"waiting{i:D2}.example", Admission.Apply, start.AddMinutes(i));
            applicant.Arrived(i + 1);
            if (i >= count) applicant.Decline(DateTimeOffset.UtcNow);
            Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(applicant).Send(Cancellation));
        }

        using var mo = await SignedIn(tangentSpace, Mo);
        var path = ApplicationsController.Path;
        // Every application leads with the declined ones; those that wait, alone, lead with the newest that waits.
        Assert.Equal("declined", (await Send(mo.Client, HttpMethod.Get, path, json: true)).Json.GetProperty("applications")[0].GetProperty("state").GetString());
        var first = await Send(mo.Client, HttpMethod.Get, path + "?state=waiting", json: true);
        var firstRows = first.Json.GetProperty("applications").EnumerateArray().ToList();
        Assert.Equal(Applications.PageSize, firstRows.Count);
        Assert.Equal(Persona.Id($"waiting{count - 1:D2}"), firstRows[0].GetProperty("id").GetString());
        Assert.All(firstRows, row => Assert.Equal("waiting", row.GetProperty("state").GetString()));
        Assert.Equal("waiting", first.Json.GetProperty("state").GetString());
        var next = first.Json.GetProperty("next").GetString()!;
        Assert.StartsWith(ApplicationsController.WaitingAddress(BaseUrl) + "&after=", next);

        var second = await Send(mo.Client, HttpMethod.Get, new Uri(next).PathAndQuery, json: true);
        Assert.Equal([Persona.Id("waiting01"), Persona.Id("waiting00")],
            second.Json.GetProperty("applications").EnumerateArray().Select(applicant => applicant.GetProperty("id").GetString()));
        Assert.True(second.Json.GetProperty("continued").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(mo.Client, HttpMethod.Get, path + "?state=declined", json: true)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(mo.Client, HttpMethod.Get, path + "?state=waiting&after=nonsense", json: true)).Status);
        var html = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, path + "?state=waiting")).Body);
        Assert.Contains("aria-pressed=\"true\" disabled>Waiting</button>", html);

        // A decision sent from those that wait comes back to them, and the one decided no longer waits there.
        var newest = Persona.Id($"waiting{count - 1:D2}");
        var admitted = await Form(mo, path + "?state=waiting", ApplicationsController.AdmitPath, new Dictionary<string, string>
        {
            [ApplicationsController.ParticipantField] = newest, [ApplicationsController.StateQuery] = "waiting"
        });
        Assert.Equal((HttpStatusCode.SeeOther, ApplicationsController.WaitingAddress(BaseUrl)), (admitted.Status, admitted.Location));
        Assert.Equal(Persona.Id($"waiting{count - 2:D2}"),
            (await Send(mo.Client, HttpMethod.Get, path + "?state=waiting", json: true)).Json.GetProperty("applications")[0].GetProperty("id").GetString());
    }
}
