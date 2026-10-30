using System.Net;
using System.Text.Json;
using Koan.Identity.Roles;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Participants;

/// <summary>The people here: whoever holds ManageStanding or ManageRoles finds the place's participants at
/// <c>/settings/people</c>, a page at a time from a sealed cursor and by the start of a handle through the handle index;
/// a moderator times a member out, who reads and posts nothing until it is lifted or passes, and bans one, for whom the
/// place is absent on a page and on an open stream; an administrator gives and takes the moderator role; nobody acts on
/// someone whose role is not below theirs, on themselves or on the operator; each change is one event that targets the
/// participant, whose door says what happened; and a profile offers the same changes.</summary>
public sealed class PeopleTests
{
    private const string Leo = "did:plc:leo4people";
    private const string Mia = "did:plc:mia4people";
    private const string Otto = "did:plc:otto4people";

    /// <summary>The writes' place, with Leo an administrator, Mia a second moderator and Otto its operator, an
    /// administrator.</summary>
    private static async Task<WriteLoop> Keepers(TangentSpaceFixture tangentSpace)
    {
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Member, Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Mia, [Roles.Member, Roles.Moderator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Otto, [Roles.Member, Roles.Administrator], ct: Cancellation);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.OperatorId = Otto;
        space.ProprietorId = Otto;
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Store<TangentSpace>(space), Cancellation));
        return writer;
    }

    private static Task<Answered> StartIn(Browser browser, string title)
    {
        var form = "/t/" + Workshop + "/new";
        return Json(browser, form, form, new { id = Tids.Mint(), title, body = "Hello, everyone." });
    }

    private static async Task<IReadOnlyList<string?>> Can(Browser browser, string path)
        => (await Send(browser.Client, HttpMethod.Get, path, json: true)).Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()).ToList();

    private static IReadOnlyList<string?> Strings(JsonElement element, string name)
        => element.GetProperty(name).EnumerateArray().Select(item => item.GetString()).ToList();

    /// <summary>The row a page of the people lists for <paramref name="id"/>.</summary>
    private static JsonElement Row(JsonElement page, string id)
        => page.GetProperty("people").EnumerateArray().Single(row => row.GetProperty("id").GetString() == id);

    private static async Task<IReadOnlyList<Event>> Since(long before)
        => (await Event.All(Cancellation)).Where(e => e.Seq > before).OrderBy(e => e.Seq).ToList();

    private static async Task<long> Head() => (await Event.All(Cancellation)).Select(e => e.Seq).DefaultIfEmpty(0).Max();

    [Fact]
    public async Task A_moderator_times_a_member_out_who_reads_and_posts_nothing_until_it_is_lifted_or_passes_and_their_door_says_so()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var ada = await SignedIn(tangentSpace, Ada);
        var path = PeopleController.Path;

        // The moderator finds the people in the settings, with time out and ban offered on a member, and no role.
        var listed = await Send(mo.Client, HttpMethod.Get, path, json: true);
        Assert.Equal(HttpStatusCode.OK, listed.Status);
        Assert.Contains("People", listed.Json.GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("name").GetString()));
        var adaRow = Row(listed.Json, Ada);
        Assert.Equal(("admitted", "person"), (adaRow.GetProperty("standing").GetString(), adaRow.GetProperty("kind").GetString()));
        Assert.Equal([People.Verb.TimeOut, People.Verb.Ban], Strings(adaRow, "can"));
        Assert.Empty(Strings(adaRow, "give"));
        Assert.Equal(["Member"], Strings(adaRow, "roles"));
        Assert.Equal(["hour", "day", "week"], Strings(listed.Json.GetProperty("forms"), "durations"));
        var html = (await Send(mo.Client, HttpMethod.Get, path)).Body;
        Assert.Contains("<summary class=\"watch-button\">Time out</summary>", html);
        Assert.Contains("href=\"" + BaseUrl + "/p/ada4writes.example\"", html);
        var before = await Head();

        var timed = await Form(mo, path, PeopleController.TimeOutPath, new Dictionary<string, string>
        {
            [PeopleController.ParticipantField] = Ada, [PeopleController.ForField] = "hour", [PeopleController.BackField] = path
        });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + path), (timed.Status, timed.Location));
        var row = (await Participant.Get(Ada, Cancellation))!;
        Assert.Equal(Standing.TimedOut, row.Standing);
        Assert.InRange(row.StandingUntil!.Value, DateTimeOffset.UtcNow.AddMinutes(55), DateTimeOffset.UtcNow.AddMinutes(61));
        var logged = Assert.Single(await Since(before));
        Assert.Equal((EventKind.StandingChanged, Mo, Ada, Standing.TimedOut, Standing.Admitted),
            (logged.Kind, logged.ActorId, logged.TargetId, logged.Standing, logged.Before));

        // Timed out, she reads and posts nothing, and her door says until when.
        Assert.Equal([nameof(Bit.View)], await Can(ada, "/t/" + Workshop));
        var refused = await StartIn(ada, "While timed out");
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.TimedOut), (refused.Status, refused.Gate));
        var door = await Send(ada.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal(Capabilities.TimedOut, door.Json.GetProperty("gate").GetString());
        Assert.Equal(row.StandingUntil!.Value.ToUnixTimeSeconds(), door.Json.GetProperty("until").GetDateTimeOffset().ToUnixTimeSeconds());
        Assert.Contains("You’re timed out until", (await Send(ada.Client, HttpMethod.Get, "/")).Body);
        var timedRow = Row((await Send(mo.Client, HttpMethod.Get, path, json: true)).Json, Ada);
        Assert.Equal("timed-out", timedRow.GetProperty("standing").GetString());
        Assert.Equal([People.Verb.Lift, People.Verb.Ban], Strings(timedRow, "can"));

        // Lifted, as JSON: the answer is her row as the moderator now finds it; she posts again and her door says she is back.
        var lifted = await Json(mo, path, PeopleController.LiftPath, new { participant = Ada });
        Assert.Equal((HttpStatusCode.OK, "admitted"), (lifted.Status, lifted.Json.GetProperty("standing").GetString()));
        Assert.Equal([People.Verb.TimeOut, People.Verb.Ban], Strings(lifted.Json, "can"));
        Assert.Equal(HttpStatusCode.Created, (await StartIn(ada, "Back again")).Status);
        var back = await Send(ada.Client, HttpMethod.Get, "/", json: true);
        Assert.False(back.Json.TryGetProperty("gate", out _));
        var news = back.Json.GetProperty("news").GetProperty("lifted");
        Assert.Equal((Mo, "timed-out"), (news.GetProperty("who").GetProperty("id").GetString(), news.GetProperty("was").GetString()));
        Assert.Contains("lifted your timeout: you’re back", (await Send(ada.Client, HttpMethod.Get, "/")).Body);
        // Lifting what is lifted writes nothing.
        var events = await Head();
        Assert.Equal(HttpStatusCode.OK, (await Json(mo, path, PeopleController.LiftPath, new { participant = Ada })).Status);
        Assert.Equal(events, await Head());

        // A timeout that has run its course is over: she posts, and the people list her admitted.
        var start = DateTimeOffset.UtcNow.AddMinutes(-61);
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new TimeOut(Mo, Ada, start.AddHours(1), start), Cancellation));
        Assert.Equal(HttpStatusCode.Created, (await StartIn(ada, "After it passed")).Status);
        Assert.Equal("admitted", Row((await Send(mo.Client, HttpMethod.Get, path, json: true)).Json, Ada).GetProperty("standing").GetString());

        // A duration that is not offered is refused, and nothing is written.
        events = await Head();
        var unknown = await Json(mo, path, PeopleController.TimeOutPath, new { participant = Ada, @for = "year" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.Status);
        Assert.Equal(events, await Head());
    }

    [Fact]
    public async Task A_ban_makes_the_place_absent_to_the_participant_on_a_page_and_on_an_open_stream_and_a_lift_brings_them_back()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var ada = await SignedIn(tangentSpace, Ada);
        await using var stream = await Streamed.Open(ada.Client, EventsController.Path);
        Assert.Equal("open", (await stream.Any()).Comment);
        var path = PeopleController.Path;
        Assert.Contains("<summary class=\"watch-button\">Ban</summary>", (await Send(mo.Client, HttpMethod.Get, path)).Body);
        var before = await Head();

        var banned = await Form(mo, path, PeopleController.BanPath, new Dictionary<string, string>
        {
            [PeopleController.ParticipantField] = Ada, [PeopleController.BackField] = path
        });

        Assert.Equal(HttpStatusCode.SeeOther, banned.Status);
        Assert.Equal(Standing.Banned, (await Participant.Get(Ada, Cancellation))!.Standing);
        var logged = Assert.Single(await Since(before));
        Assert.Equal((EventKind.StandingChanged, Mo, Ada, Standing.Banned), (logged.Kind, logged.ActorId, logged.TargetId, logged.Standing));
        // Her open stream ends at the row that bans her, which it does not send; every page answers as nothing there.
        await Assert.ThrowsAsync<EndOfStreamException>(() => stream.Next());
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/t/" + Workshop, json: true)).Status);
        await using (var again = await Streamed.Open(ada.Client, EventsController.Path))
            Assert.Equal(HttpStatusCode.NotFound, again.Response.StatusCode);
        var bannedRow = Row((await Send(mo.Client, HttpMethod.Get, path, json: true)).Json, Ada);
        Assert.Equal("banned", bannedRow.GetProperty("standing").GetString());
        Assert.Equal([People.Verb.Lift], Strings(bannedRow, "can"));
        // A banned participant is lifted before anything else.
        var timedOut = await Json(mo, path, PeopleController.TimeOutPath, new { participant = Ada, @for = "day" });
        Assert.Equal((HttpStatusCode.Conflict, Participant.BannedFirst), (timedOut.Status, timedOut.Json.GetProperty("detail").GetString()));

        Assert.Equal(HttpStatusCode.OK, (await Json(mo, path, PeopleController.LiftPath, new { participant = Ada })).Status);
        var door = await Send(ada.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal(HttpStatusCode.OK, door.Status);
        Assert.Equal("banned", door.Json.GetProperty("news").GetProperty("lifted").GetProperty("was").GetString());
        Assert.Contains("lifted your ban", (await Send(ada.Client, HttpMethod.Get, "/")).Body);
        Assert.Equal(HttpStatusCode.Created, (await StartIn(ada, "I am back")).Status);
    }

    [Fact]
    public async Task A_moderator_acts_on_members_alone_never_on_an_administrator_another_moderator_themselves_or_the_operator()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Keepers(tangentSpace);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var leo = await SignedIn(tangentSpace, Leo);
        var path = PeopleController.Path;
        var before = await Head();

        // The rows offer the moderator nothing on any of them.
        var page = (await Send(mo.Client, HttpMethod.Get, path, json: true)).Json;
        foreach (var id in new[] { Leo, Mia, Mo, Otto })
        {
            var row = Row(page, id);
            Assert.Empty(Strings(row, "can"));
            Assert.Empty(Strings(row, "give"));
            Assert.Empty(Strings(row, "take"));
        }

        foreach (var (target, reason) in new[] { (Leo, People.NotBelow), (Mia, People.NotBelow), (Mo, People.Themselves), (Otto, People.Operator) })
        {
            foreach (var verb in new[] { PeopleController.BanPath, PeopleController.LiftPath })
            {
                var refused = await Json(mo, path, verb, new { participant = target });
                Assert.Equal((HttpStatusCode.Forbidden, reason), (refused.Status, refused.Gate));
            }

            var timed = await Json(mo, path, PeopleController.TimeOutPath, new { participant = target, @for = "hour" });
            Assert.Equal((HttpStatusCode.Forbidden, reason), (timed.Status, timed.Gate));
            Assert.Equal(Standing.Admitted, (await Participant.Get(target, Cancellation))!.Standing);
        }

        // A form refused reads the page again, the reason above the list.
        var form = await Form(mo, path, PeopleController.BanPath, new Dictionary<string, string> { [PeopleController.ParticipantField] = Leo });
        Assert.Equal(HttpStatusCode.Forbidden, form.Status);
        Assert.Contains(People.NotBelow, form.Body);

        // An administrator acts on moderators and members, never on another administrator or the operator.
        Assert.Equal([People.Verb.TimeOut, People.Verb.Ban], Strings(Row((await Send(leo.Client, HttpMethod.Get, path, json: true)).Json, Mia), "can"));
        Assert.Equal((HttpStatusCode.Forbidden, People.Operator), Refusal(await Json(leo, path, PeopleController.BanPath, new { participant = Otto })));
        Assert.Equal(HttpStatusCode.NotFound, (await Json(mo, path, PeopleController.BanPath, new { participant = "did:plc:nobody" })).Status);
        Assert.Empty(await Since(before));
    }

    private static (HttpStatusCode, string?) Refusal(Answered answered) => (answered.Status, answered.Gate);

    [Fact]
    public async Task An_administrator_makes_a_member_a_moderator_who_then_times_out_a_member_and_takes_it_back_and_the_operator_alone_takes_an_administrators_role()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Keepers(tangentSpace);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var bea = await SignedIn(tangentSpace, Bea);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var otto = await SignedIn(tangentSpace, Otto);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        var path = PeopleController.Path;

        // A moderator holds no ManageRoles: no role is offered, and giving one is refused with its gate.
        Assert.Empty(Strings(Row((await Send(mo.Client, HttpMethod.Get, path, json: true)).Json, Bea), "give"));
        var gated = await Json(mo, path, PeopleController.GivePath, new { participant = Bea, role = People.Moderator });
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageRoles)), (gated.Status, gated.Gate));

        // The administrator is offered both roles on a member, and gives the moderator role from the people page.
        var beaRow = Row((await Send(leo.Client, HttpMethod.Get, path, json: true)).Json, Bea);
        Assert.Equal([People.Moderator, People.Administrator], Strings(beaRow, "give"));
        Assert.Contains("Make moderator", (await Send(leo.Client, HttpMethod.Get, path)).Body);
        var before = await Head();
        var given = await Form(leo, path, PeopleController.GivePath, new Dictionary<string, string>
        {
            [PeopleController.ParticipantField] = Bea, [PeopleController.RoleField] = People.Moderator, [PeopleController.BackField] = path
        });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + path), (given.Status, given.Location));
        Assert.True((await roles.Bag(Bea, authenticated: true, Cancellation)).InRole(Roles.Moderator));
        var logged = Assert.Single(await Since(before));
        Assert.Equal((EventKind.RoleChanged, Leo, Bea, Roles.Moderator, (bool?)true), (logged.Kind, logged.ActorId, logged.TargetId, logged.Role, logged.Given));
        var door = (await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("roles");
        Assert.Equal((Leo, People.Moderator, true), (door[0].GetProperty("who").GetProperty("id").GetString(), door[0].GetProperty("role").GetString(),
            door[0].GetProperty("given").GetBoolean()));
        Assert.Contains("made you a moderator", (await Send(bea.Client, HttpMethod.Get, "/")).Body);
        // Giving what is held writes nothing.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, path, PeopleController.GivePath, new { participant = Bea, role = People.Moderator })).Status);
        Assert.Single(await Since(before));

        // The new moderator times out a member.
        var timed = await Json(bea, path, PeopleController.TimeOutPath, new { participant = Ada, @for = "day" });
        Assert.Equal((HttpStatusCode.OK, "timed-out"), (timed.Status, timed.Json.GetProperty("standing").GetString()));

        // Taken back, as JSON: her door says so, and she times out no one.
        var taken = await Json(leo, path, PeopleController.TakePath, new { participant = Bea, role = People.Moderator });
        Assert.Equal(HttpStatusCode.OK, taken.Status);
        Assert.DoesNotContain("Moderator", Strings(taken.Json, "roles"));
        Assert.Contains("took your moderator role", (await Send(bea.Client, HttpMethod.Get, "/")).Body);
        var refused = await Json(bea, TangentSpaceSettingsController.Path, PeopleController.LiftPath, new { participant = Ada });
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageStanding)), (refused.Status, refused.Gate));
        // The member role is admission's, never given here.
        Assert.Equal(HttpStatusCode.BadRequest, (await Json(leo, path, PeopleController.TakePath, new { participant = Ada, role = "member" })).Status);

        // An administrator never takes another's role, nor the operator's; the operator, above every role, takes theirs.
        Assert.Empty(Strings(Row((await Send(leo.Client, HttpMethod.Get, path, json: true)).Json, Otto), "take"));
        var operatorRefused = await Json(leo, path, PeopleController.TakePath, new { participant = Otto, role = People.Administrator });
        Assert.Equal((HttpStatusCode.Forbidden, People.Operator), (operatorRefused.Status, operatorRefused.Gate));
        Assert.Equal([People.Administrator], Strings(Row((await Send(otto.Client, HttpMethod.Get, path, json: true)).Json, Leo), "take"));
        Assert.Equal(HttpStatusCode.OK, (await Json(otto, path, PeopleController.TakePath, new { participant = Leo, role = People.Administrator })).Status);
        Assert.False((await roles.Bag(Leo, authenticated: true, Cancellation)).InRole(Roles.Administrator));
        Assert.True((await roles.Bag(Otto, authenticated: true, Cancellation)).InRole(Roles.Administrator));
    }

    [Fact]
    public async Task The_people_are_read_a_bounded_page_at_a_time_from_a_sealed_cursor_and_by_the_start_of_a_handle_through_its_index()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        for (var i = 0; i < People.PageSize + 2; i++)
        {
            var person = new Participant { Id = $"did:plc:person{i:D2}", Handle = $"person{i:D2}.example" };
            Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(person), Cancellation));
        }

        var applicant = Participant.Arrive("did:plc:applicant", "applicant.example", Admission.Apply);
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(applicant), Cancellation));
        using var mo = await SignedIn(tangentSpace, Mo);
        var path = PeopleController.Path;

        var first = await Send(mo.Client, HttpMethod.Get, path, json: true);
        var firstHandles = first.Json.GetProperty("people").EnumerateArray().Select(row => row.GetProperty("handle").GetString()!).ToList();
        Assert.Equal(People.PageSize, firstHandles.Count);
        Assert.Equal(firstHandles.Order(StringComparer.Ordinal), firstHandles);
        Assert.False(first.Json.GetProperty("continued").GetBoolean());
        var next = first.Json.GetProperty("next").GetString()!;
        // The cursor says nothing of the handle it continues from.
        Assert.DoesNotContain(firstHandles[^1], Uri.UnescapeDataString(next));

        var second = await Send(mo.Client, HttpMethod.Get, new Uri(next).PathAndQuery, json: true);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.True(second.Json.GetProperty("continued").GetBoolean());
        var secondHandles = second.Json.GetProperty("people").EnumerateArray().Select(row => row.GetProperty("handle").GetString()!).ToList();
        var all = firstHandles.Concat(secondHandles).ToList();
        // Every admitted participant once, in handle order, and no applicant.
        Assert.Equal(People.PageSize + 2 + 3, all.Count);
        Assert.Equal(all.Order(StringComparer.Ordinal), all);
        Assert.DoesNotContain("applicant.example", all);
        Assert.False(second.Json.TryGetProperty("next", out var none) && none.ValueKind != JsonValueKind.Null);

        // A cursor this server did not seal, or one altered, is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(mo.Client, HttpMethod.Get, path + "?after=nonsense", json: true)).Status);
        var sealedCursor = Uri.UnescapeDataString(new Uri(next).Query.Split("after=")[1]);
        var altered = sealedCursor[..^2] + (sealedCursor[^2] == 'A' ? 'B' : 'A') + sealedCursor[^1];
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(mo.Client, HttpMethod.Get, path + "?after=" + Uri.EscapeDataString(altered), json: true)).Status);

        // The start of a handle, typed as a person types it, finds those whose handles start with it.
        var found = await Send(mo.Client, HttpMethod.Get, path + "?handle=" + Uri.EscapeDataString(" @Person2"), json: true);
        Assert.Equal(Enumerable.Range(20, People.PageSize + 2 - 20).Select(i => $"did:plc:person{i:D2}"),
            found.Json.GetProperty("people").EnumerateArray().Select(row => row.GetProperty("id").GetString()));
        Assert.Equal("person2", found.Json.GetProperty("handle").GetString());
        Assert.Empty((await Send(mo.Client, HttpMethod.Get, path + "?handle=nobody", json: true)).Json.GetProperty("people").EnumerateArray());
        Assert.Contains("No one here has a handle that starts with nobody.", (await Send(mo.Client, HttpMethod.Get, path + "?handle=nobody")).Body);

        // The range a start reads is one search of the handle index.
        var plan = HandlePlan(tangentSpace.DatabasePath, "person2", "person3");
        Assert.Contains(plan, detail => detail.StartsWith("SEARCH", StringComparison.Ordinal) && detail.Contains("USING INDEX ix_participant_handles", StringComparison.Ordinal));

        // A member reads no one, and is refused with the gate.
        using var ada = await SignedIn(tangentSpace, Ada);
        var refused = await Send(ada.Client, HttpMethod.Get, path, json: true);
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageStanding)), (refused.Status, refused.Json.GetProperty("gate").GetString()));
        Assert.Empty(refused.Json.GetProperty("people").EnumerateArray());
        Assert.DoesNotContain("People", (await Send(ada.Client, HttpMethod.Get, TangentSpaceSettingsController.Path, json: true)).Json
            .GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task A_profile_offers_whoever_may_change_the_participant_the_same_changes_inline_and_comes_back_to_the_profile()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var bea = await SignedIn(tangentSpace, Bea);
        using var ada = await SignedIn(tangentSpace, Ada);
        var profile = "/p/ada4writes.example";

        var read = await Send(mo.Client, HttpMethod.Get, profile, json: true);
        Assert.Equal([People.Verb.TimeOut, People.Verb.Ban], Strings(read.Json.GetProperty("listed"), "can"));
        Assert.Equal(BaseUrl + PeopleController.TimeOutPath, read.Json.GetProperty("forms").GetProperty("timeOut").GetString());
        var html = (await Send(mo.Client, HttpMethod.Get, profile)).Body;
        Assert.Contains("is admitted here.", html);
        Assert.Contains("<summary class=\"watch-button\">Ban</summary>", html);

        var timed = await Form(mo, profile, PeopleController.TimeOutPath, new Dictionary<string, string>
        {
            [PeopleController.ParticipantField] = Ada, [PeopleController.ForField] = "week", [PeopleController.BackField] = profile
        });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + profile), (timed.Status, timed.Location));
        Assert.Contains("is timed out here until", (await Send(mo.Client, HttpMethod.Get, profile)).Body);

        // A member reads the profile without anyone's standing or a change; a page outside the place's is never where a form returns.
        var member = await Send(bea.Client, HttpMethod.Get, profile, json: true);
        Assert.False(member.Json.TryGetProperty("listed", out var listed) && listed.ValueKind != JsonValueKind.Null);
        var elsewhere = await Form(mo, profile, PeopleController.LiftPath, new Dictionary<string, string>
        {
            [PeopleController.ParticipantField] = Ada, [PeopleController.BackField] = "//elsewhere.example/p/x"
        });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + PeopleController.Path), (elsewhere.Status, elsewhere.Location));
    }

    /// <summary>The plan SQLite gives the read of a range of handles as Koan's connector composes it: the handle read as
    /// the index declares it, the range, its order and a limit.</summary>
    private static List<string> HandlePlan(string database, string from, string to)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var find = connection.CreateCommand();
        find.CommandText = "SELECT tbl_name, sql FROM sqlite_master WHERE type = 'index' AND name = 'ix_participant_handles'";
        string table, handle;
        using (var row = find.ExecuteReader())
        {
            Assert.True(row.Read(), "No index is named ix_participant_handles.");
            table = row.GetString(0);
            var sql = row.GetString(1);
            var open = sql.IndexOf(" (", sql.IndexOf(" ON ", StringComparison.Ordinal), StringComparison.Ordinal) + 2;
            handle = sql[open..^1].Trim();
        }

        using var explain = connection.CreateCommand();
        explain.CommandText = $"EXPLAIN QUERY PLAN SELECT * FROM \"{table}\" WHERE {handle} >= '{from}' AND {handle} < '{to}' ORDER BY {handle} LIMIT {People.PageSize + 1}";
        using var reader = explain.ExecuteReader();
        var details = new List<string>();
        while (reader.Read()) details.Add(reader.GetString(3));
        return details;
    }
}
