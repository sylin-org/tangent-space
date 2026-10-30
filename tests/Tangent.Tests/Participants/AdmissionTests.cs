using System.Net;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;
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
    private const string Grace = "did:plc:grace4admission";
    private const string Hal = "did:plc:hal4admission";
    private const string Ivy = "did:plc:ivy4admission";

    private static async Task<bool> IsMember(TangentSpaceFixture tangentSpace, string id)
        => (await tangentSpace.Services.GetRequiredService<RoleCollection>().Bag(id, authenticated: true, Cancellation)).InRole(Roles.Member);

    private static async Task<IReadOnlyList<string?>> Can(Browser browser, string path)
        => (await Send(browser.Client, HttpMethod.Get, path, json: true)).Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()).ToList();

    /// <summary>Starts a Topic in <paramref name="tangent"/> as JSON, with the session's token from the Tangent's page, which
    /// carries a form for every signed-in reader, so an applicant the new-topic page offers no form still sends one.</summary>
    private static Task<Answered> StartIn(Browser browser, string tangent, string title)
        => Json(browser, "/t/" + tangent, "/t/" + tangent + "/new", new { id = Tids.Mint(), title, body = "Hello, everyone." });

    [Fact]
    public async Task On_a_join_place_a_newcomer_who_signs_in_is_a_member_and_starts_a_topic_in_general()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        // First run, as the first person to sign in sets the place up: General is there from its first minute.
        using var leo = new Browser(tangentSpace);
        (await leo.SignIn("leo", "did:plc:leo4admission", Cancellation, handle: "leo.example")).Dispose();
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
        const string leoId = "did:plc:leo4admission";
        await Given.AParticipant(tangentSpace, leoId, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, leoId);
        using var ada = await SignedIn(tangentSpace, Ada);
        var path = TangentSpaceSettingsController.AdmissionPath;

        var page = await Send(leo.Client, HttpMethod.Get, path, json: true);
        Assert.Equal((HttpStatusCode.OK, "join"), (page.Status, page.Json.GetProperty("admission").GetString()));
        Assert.Equal(["join", "apply"], page.Json.GetProperty("admissions").EnumerateArray().Select(word => word.GetString()));
        Assert.Equal(["General", "Admission", "Applications", "Appearance", "People", "Roles"], page.Json.GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("name").GetString()));
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
        Assert.Equal((HttpStatusCode.BadRequest, (string?)null), (unknown.Status, unknown.Gate));
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, path, path, new { admission = "join" })).Status);
        Assert.Equal(before + 2, await Event.Count.Exact(Cancellation));

        // A member reads the choice and changes nothing, and their rail lists nothing, for they may use none of it.
        var read = await Send(ada.Client, HttpMethod.Get, path, json: true);
        Assert.Equal((HttpStatusCode.OK, Capabilities.NoRoleGrants(Bit.ManageSpace)), (read.Status, read.Json.GetProperty("gate").GetString()));
        Assert.Equal(0, read.Json.GetProperty("sections").GetArrayLength());
        var refused = await Json(ada, "/t/" + Workshop, path, new { admission = "apply" });
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageSpace)), (refused.Status, refused.Gate));
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
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.Apply), (started.Status, started.Gate));
        Assert.Equal(0, await Post.Count.Exact(Cancellation));

        var door = await Send(grace.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal(Capabilities.Apply, door.Json.GetProperty("gate").GetString());
        Assert.Equal([nameof(Bit.View)], door.Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));
        var html = await Send(grace.Client, HttpMethod.Get, "/");
        Assert.Contains("Your application is waiting", html.Body);
        Assert.Contains($"data-gate=\"{Capabilities.Apply}\"", html.Body);
    }

    [Fact]
    public async Task A_holder_of_manage_standing_admits_one_applicant_who_then_posts_and_declines_another_each_a_targeted_event_and_anyone_else_is_refused()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.ATangent(tangentSpace, General, ct: Cancellation);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Admission = Admission.Apply;
        Assert.IsType<Outcome<TangentSpace>.Done>(await tangentSpace.Services.GetRequiredService<WriteLoop>().Send(new Store<TangentSpace>(space), Cancellation));
        using var grace = await SignedIn(tangentSpace, Grace);
        using var hal = await SignedIn(tangentSpace, Hal);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var ada = await SignedIn(tangentSpace, Ada);
        var path = ApplicationsController.Path;

        // The moderator holds ManageStanding: the account menu opens the applications, newest first.
        Assert.Equal(BaseUrl + path, (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("who").GetProperty("settings").GetString());
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
        Assert.Contains("<summary class=\"watch-button\">Decline</summary>", html.Body);
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
        Assert.Equal(Capabilities.Declined, halDoor.Json.GetProperty("gate").GetString());
        Assert.Contains("Your application was declined", (await Send(hal.Client, HttpMethod.Get, "/")).Body);
        var halStarted = await StartIn(hal, General, "Still here");
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.Declined), (halStarted.Status, halStarted.Gate));

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
        Assert.Equal(HttpStatusCode.NotFound, (await Json(mo, settings, ApplicationsController.AdmitPath, new { participant = "did:plc:nobody" })).Status);
        Assert.Equal(events, await Event.Count.Exact(Cancellation));

        // A member sees no applications, in the rail or the page, and is refused.
        var refusedPage = await Send(ada.Client, HttpMethod.Get, path, json: true);
        var gate = Capabilities.NoRoleGrants(Bit.ManageStanding);
        Assert.Equal((HttpStatusCode.Forbidden, gate), (refusedPage.Status, refusedPage.Json.GetProperty("gate").GetString()));
        Assert.Empty(refusedPage.Json.GetProperty("applications").EnumerateArray());
        Assert.DoesNotContain("ivy4admission", (await Send(ada.Client, HttpMethod.Get, path)).Body);
        Assert.DoesNotContain("Applications", (await Send(ada.Client, HttpMethod.Get, TangentSpaceSettingsController.Path, json: true)).Json
            .GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("name").GetString()));
        var refused = await Json(ada, "/t/" + General, ApplicationsController.AdmitPath, new { participant = Ivy });
        Assert.Equal((HttpStatusCode.Forbidden, gate), (refused.Status, refused.Gate));
        Assert.Equal(Standing.Applied, (await Participant.Get(Ivy, Cancellation))!.Standing);
        using var visitor = Agent(tangentSpace);
        var visiting = await Send(visitor, HttpMethod.Get, path, json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (visiting.Status, visiting.Json.GetProperty("gate").GetString()));
        Assert.Equal(events, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task The_applications_are_read_a_bounded_page_at_a_time_newest_first_through_a_cursor()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var start = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var count = Applications.PageSize + 2;
        for (var i = 0; i < count; i++)
        {
            var applicant = Participant.Arrive($"did:plc:applicant{i:D2}", $"applicant{i:D2}.example", Admission.Apply, start.AddMinutes(i));
            Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(applicant), Cancellation));
        }

        using var mo = await SignedIn(tangentSpace, Mo);
        var first = await Send(mo.Client, HttpMethod.Get, ApplicationsController.Path, json: true);
        var firstIds = first.Json.GetProperty("applications").EnumerateArray().Select(applicant => applicant.GetProperty("id").GetString()).ToList();
        Assert.Equal(Applications.PageSize, firstIds.Count);
        Assert.Equal($"did:plc:applicant{count - 1:D2}", firstIds[0]);
        var next = first.Json.GetProperty("next").GetString()!;
        Assert.False(first.Json.GetProperty("continued").GetBoolean());

        var second = await Send(mo.Client, HttpMethod.Get, new Uri(next).PathAndQuery, json: true);
        Assert.Equal(["did:plc:applicant01", "did:plc:applicant00"],
            second.Json.GetProperty("applications").EnumerateArray().Select(applicant => applicant.GetProperty("id").GetString()));
        Assert.True(second.Json.GetProperty("continued").GetBoolean());
        Assert.False(second.Json.TryGetProperty("next", out var none) && none.ValueKind != System.Text.Json.JsonValueKind.Null);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(mo.Client, HttpMethod.Get, ApplicationsController.Path + "?after=nonsense", json: true)).Status);
    }
}
