using System.Net;
using System.Net.Http.Headers;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Space;

/// <summary>The place's settings at <c>/settings</c>: offered in the account menu to whoever holds ManageSpace; general
/// renames the place and writes the line under its name on the door, appending space.changed; a refused name keeps the
/// form; anyone else is refused with the gate; the rail lists only what its reader may use, and a person sent to a section
/// they may not use lands on the first they may; and the settings answer JSON with <c>can</c> and <c>gate</c>.</summary>
public sealed class TangentSpaceSettingsTests
{
    private const string Leo = "did:plc:leo4place";

    private static Dictionary<string, string> General(string name, string description) => new()
    {
        [TangentSpaceSettingsController.NameField] = name, [TangentSpaceSettingsController.DescriptionField] = description
    };

    [Fact]
    public async Task Whoever_holds_manage_space_renames_and_describes_the_place_from_the_account_menu_and_space_changed_is_appended()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        var settings = BaseUrl + TangentSpaceSettingsController.Path;

        // The account menu offers the settings on every page, and the door names them for an agent.
        Assert.Contains($"<a class=\"account-item\" href=\"{settings}\">", (await Send(leo.Client, HttpMethod.Get, "/")).Body);
        Assert.Contains($"<a class=\"account-item\" href=\"{settings}\">", (await Send(leo.Client, HttpMethod.Get, "/t/workshop")).Body);
        Assert.Equal(settings, (await Send(leo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("who").GetProperty("settings").GetString());

        var page = await Send(leo.Client, HttpMethod.Get, TangentSpaceSettingsController.Path, json: true);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(("Field notes", ""), (page.Json.GetProperty("name").GetString(), page.Json.GetProperty("description").GetString()));
        Assert.Contains(nameof(Bit.ManageSpace), page.Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));
        Assert.False(page.Json.TryGetProperty("gate", out _));
        Assert.Equal([("General", settings, true), ("Admission", settings + "/admission", false), ("Applications", settings + "/applications", false),
                ("Appearance", settings + "/appearance", false), ("People", settings + "/people", false), ("Roles", settings + "/roles", false), ("Export", settings + "/export", false)],
            page.Json.GetProperty("sections").EnumerateArray().Select(section =>
                (section.GetProperty("name").GetString(), section.GetProperty("address").GetString(), section.GetProperty("current").GetBoolean())));
        var before = await Event.Count.Exact(Cancellation);

        var saved = await Form(leo, TangentSpaceSettingsController.Path, TangentSpaceSettingsController.Path,
            General("Lantern Works", "Kilns, glazes and the people who fire them."));

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/"), (saved.Status, saved.Location));
        var row = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        Assert.Equal(("Lantern Works", "Kilns, glazes and the people who fire them.", 2L), (row.Name, row.Description, row.Version));
        var logged = Assert.Single(await Event.All(Cancellation), e => e.Seq > before);
        Assert.Equal((EventKind.SpaceChanged, Leo), (logged.Kind, logged.ActorId));
        var door = await Send(leo.Client, HttpMethod.Get, "/");
        Assert.Contains("<h1 id=\"door-title\" class=\"tangent-title\">Lantern Works</h1>", door.Body);
        Assert.Contains("<p class=\"tangent-description door-description\">Kilns, glazes and the people who fire them.</p>", door.Body);
        Assert.Equal("Kilns, glazes and the people who fire them.", (await Send(leo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("description").GetString());

        // As JSON, the description alone; the name stays.
        var described = await Json(leo, TangentSpaceSettingsController.Path, TangentSpaceSettingsController.Path, new { description = "Fired weekly." });
        Assert.Equal(HttpStatusCode.OK, described.Status);
        Assert.Equal(("Lantern Works", "Fired weekly.", BaseUrl + "/"),
            (described.Json.GetProperty("name").GetString(), described.Json.GetProperty("description").GetString(), described.Json.GetProperty("door").GetString()));
        Assert.Contains(nameof(Bit.ManageSpace), described.Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));
        Assert.Equal(before + 2, await Event.Count.Exact(Cancellation));

        // A change of nothing writes nothing.
        Assert.Equal(HttpStatusCode.SeeOther, (await Form(leo, TangentSpaceSettingsController.Path, TangentSpaceSettingsController.Path,
            General("Lantern Works", "Fired weekly."))).Status);
        Assert.Equal(before + 2, await Event.Count.Exact(Cancellation));

        // A refused name keeps the form, and what was sent, with the reason under the name.
        var refused = await Form(leo, TangentSpaceSettingsController.Path, TangentSpaceSettingsController.Path, General("   ", "Still fired weekly."));
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Contains("id=\"space-settings-name-failure\"", refused.Body);
        Assert.Contains(TangentSpace.NameRule, refused.Body);
        Assert.Contains(">Still fired weekly.</textarea>", refused.Body);
        var tooLong = await Json(leo, TangentSpaceSettingsController.Path, TangentSpaceSettingsController.Path,
            new { description = new string('w', TangentSpace.DescriptionLimit + 1) });
        Assert.Equal((HttpStatusCode.BadRequest, (string?)null), (tooLong.Status, tooLong.Gate));
        Assert.Equal(("Lantern Works", "Fired weekly."), ((await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Name,
            (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Description));
        Assert.Equal(before + 2, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_participant_without_manage_space_has_no_settings_entry_and_is_refused_with_the_gate_and_a_visitor_is_told_to_sign_in()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var gate = Capabilities.NoRoleGrants(Bit.ManageSpace);

        Assert.DoesNotContain($"href=\"{BaseUrl}{TangentSpaceSettingsController.Path}\"", (await Send(ada.Client, HttpMethod.Get, "/")).Body);
        Assert.False((await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("who").TryGetProperty("settings", out _));

        var page = await Send(ada.Client, HttpMethod.Get, TangentSpaceSettingsController.Path, json: true);
        Assert.Equal((HttpStatusCode.OK, gate), (page.Status, page.Json.GetProperty("gate").GetString()));
        Assert.DoesNotContain(nameof(Bit.ManageSpace), page.Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));
        var html = await Send(ada.Client, HttpMethod.Get, TangentSpaceSettingsController.Path);
        Assert.Contains($"data-gate=\"{gate}\"", html.Body);
        Assert.DoesNotContain("type=\"submit\"", html.Body);

        var before = await Event.Count.Exact(Cancellation);
        // The settings offer Ada no form, so her session's token comes from the workshop's page, which carries one.
        var refused = await Form(ada, "/t/" + Workshop, TangentSpaceSettingsController.Path, General("Ada's place", ""));
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Contains($"data-gate=\"{gate}\"", refused.Body);
        var asJson = await Json(ada, "/t/" + Workshop, TangentSpaceSettingsController.Path, new { name = "Ada's place" });
        Assert.Equal((HttpStatusCode.Forbidden, gate), (asJson.Status, asJson.Gate));

        using var visitor = Agent(tangentSpace);
        var read = await Send(visitor, HttpMethod.Get, TangentSpaceSettingsController.Path, json: true);
        Assert.Equal(Capabilities.SignIn, read.Json.GetProperty("gate").GetString());
        var posted = await Send(visitor, HttpMethod.Post, TangentSpaceSettingsController.Path, JsonBody(new { name = "Nobody's place" }), json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (posted.Status, posted.Gate));

        Assert.Equal("Field notes", (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Name);
        Assert.Equal(before, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_moderators_rail_lists_only_what_they_may_use_and_general_and_admission_send_them_to_the_first_of_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        const string mod = "did:plc:mo4place";
        await Given.AParticipant(tangentSpace, mod, [Roles.Member, Roles.Moderator], ct: Cancellation);
        using var mo = await SignedIn(tangentSpace, mod);
        var applications = BaseUrl + ApplicationsController.Path;

        Assert.Equal((HttpStatusCode.SeeOther, applications), Sent(await Send(mo.Client, HttpMethod.Get, TangentSpaceSettingsController.Path)));
        Assert.Equal((HttpStatusCode.SeeOther, applications), Sent(await Send(mo.Client, HttpMethod.Get, TangentSpaceSettingsController.AdmissionPath)));

        var landed = await Send(mo.Client, HttpMethod.Get, ApplicationsController.Path);
        Assert.Equal(HttpStatusCode.OK, landed.Status);
        Assert.Contains($"href=\"{applications}\"", landed.Body);
        Assert.Contains($"href=\"{BaseUrl}{PeopleController.Path}\"", landed.Body);
        Assert.DoesNotContain($">{TangentSpaceSettingsController.General}<", landed.Body);
        Assert.DoesNotContain($">{TangentSpaceSettingsController.AdmissionSection}<", landed.Body);
        Assert.DoesNotContain($">{TangentSpaceSettingsController.RolesSection}<", landed.Body);

        // An agent reading as JSON reads the gate and the sections it may use, and the account menu opens the first of them.
        var read = await Send(mo.Client, HttpMethod.Get, TangentSpaceSettingsController.Path, json: true);
        Assert.Equal((HttpStatusCode.OK, Capabilities.NoRoleGrants(Bit.ManageSpace)), (read.Status, read.Json.GetProperty("gate").GetString()));
        Assert.Equal([TangentSpaceSettingsController.ApplicationsSection, TangentSpaceSettingsController.PeopleSection],
            read.Json.GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("name").GetString()));
        Assert.Equal(applications, (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("who").GetProperty("settings").GetString());
    }

    [Fact]
    public async Task A_member_who_may_use_no_section_reads_general_without_a_rail_under_a_plain_sentence()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);

        var page = await Send(ada.Client, HttpMethod.Get, TangentSpaceSettingsController.Path);
        var html = WebUtility.HtmlDecode(page.Body);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.DoesNotContain("aria-label=\"Settings\"", html);
        Assert.Contains("This isn\u2019t open to you here.", html);
        Assert.DoesNotContain(">no role grants", html);
        Assert.DoesNotContain("here: no role grants", html);
    }

    private static (HttpStatusCode Status, string Location) Sent(Answered answered) => (answered.Status, answered.Location ?? "");

    /// <summary>A page as a browser asks for it, with the header that asks search engines not to index it, if any.</summary>
    private static async Task<(HttpStatusCode Status, string Body, string? Robots)> Page(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await client.SendAsync(request, Cancellation);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Cancellation),
            response.Headers.TryGetValues(PrivatePlaceGate.RobotsHeader, out var robots) ? string.Join(",", robots) : null);
    }

    [Fact]
    public async Task A_private_place_serves_a_visitor_only_the_sign_in_and_asks_that_nothing_be_indexed_while_its_participants_read_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Started(writer, Ada, "What should we build?", "Kilns, glazes, lanterns.");
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var visitor = Agent(tangentSpace);
        Assert.Equal("public", (await Send(leo.Client, HttpMethod.Get, TangentSpaceSettingsController.Path, json: true)).Json.GetProperty("audience").GetString());
        Assert.Contains("What should we build?", (await Page(visitor, "/")).Body);

        // Made private from the general section: one space.changed.
        var before = await Event.Count.Exact(Cancellation);
        var made = await Form(leo, TangentSpaceSettingsController.Path, TangentSpaceSettingsController.Path, new Dictionary<string, string>
        {
            [TangentSpaceSettingsController.AudienceField] = "private"
        });
        Assert.Equal(HttpStatusCode.SeeOther, made.Status);
        Assert.Equal(Audience.Private, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Audience);
        Assert.Equal(EventKind.SpaceChanged, Assert.Single(await Event.All(Cancellation), e => e.Seq > before).Kind);

        // A visitor reads the sign-in at every address, which brings them back there, and is told no page is to be indexed.
        foreach (var path in new[] { "/", "/t/" + Workshop, "/t/" + Workshop + "/what-should-we-build", "/t/nowhere", "/p/" + Ada.Split(':')[^1] + ".example" })
        {
            var (status, body, robots) = await Page(visitor, path);
            Assert.Equal((HttpStatusCode.Unauthorized, "noindex"), (status, robots));
            Assert.Contains("Sign in to read this place", body);
            Assert.Contains("return=" + Uri.EscapeDataString(path), body);
            Assert.DoesNotContain("What should we build?", body);
        }

        var asJson = await Send(visitor, HttpMethod.Get, "/t/" + Workshop, json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (asJson.Status, asJson.Gate));
        var posted = await Send(visitor, HttpMethod.Post, "/t/" + Workshop + "/new", JsonBody(new { title = "Hello" }), json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (posted.Status, posted.Gate));
        Assert.Equal(HttpStatusCode.OK, (await Page(visitor, "/sign-in")).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/tangent.json", json: true)).Status);

        // Its participants read it as they did, and are told the same of indexing.
        var read = await Page(ada.Client, "/t/" + Workshop);
        Assert.Equal((HttpStatusCode.OK, "noindex"), (read.Status, read.Robots));
        Assert.Contains("What should we build?", read.Body);

        // Public again, as JSON: the visitor reads, and nothing asks against indexing.
        var opened = await Json(leo, TangentSpaceSettingsController.Path, TangentSpaceSettingsController.Path, new { audience = "public" });
        Assert.Equal((HttpStatusCode.OK, "public"), (opened.Status, opened.Json.GetProperty("audience").GetString()));
        var again = await Page(visitor, "/");
        Assert.Equal((HttpStatusCode.OK, null), (again.Status, again.Robots));
        Assert.Contains("What should we build?", again.Body);
        Assert.Equal(HttpStatusCode.BadRequest, (await Json(leo, TangentSpaceSettingsController.Path, TangentSpaceSettingsController.Path,
            new { audience = "members" })).Status);
    }
}
