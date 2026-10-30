using System.Net;
using Tangent.Access;
using Tangent.Events;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Space;

/// <summary>The place's settings at <c>/settings</c>: offered in the account menu to whoever holds ManageSpace; general
/// renames the place and writes the line under its name on the door, appending space.changed; a refused name keeps the
/// form; anyone else is refused with the gate; and the settings answer JSON with <c>can</c> and <c>gate</c>.</summary>
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
                ("People", settings + "/people", false)],
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
}
