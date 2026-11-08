using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Space;
using Tangent.Tools;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Tangents;

/// <summary>The Tangents on Koan's entity controller: it serves only the operations it declares, writes only what each names,
/// keeps a member sent as null, refuses a word that is not one by its field, lets no caller name a partition, sets a private Tangent's joining as one of the Tangent's own fields on both doors, and lists a
/// members-only Tangent that may be joined to whoever takes part in the place and to no visitor.</summary>
public sealed class TangentsEndpointTests
{
    private static readonly string Leo = Persona.Id("leo4endpoint");

    [Fact]
    public async Task The_tangents_serve_only_what_they_declare_and_write_only_what_each_names()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);

        // Nothing the entity controller would serve beside what the Tangents declare answers.
        Assert.True((await Json(leo, "/t", HttpMethod.Put, "/t/" + Workshop, new { name = "Taken" })).Status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        Assert.True((await Json(leo, "/t", HttpMethod.Post, "/t/bulk", new[] { new { name = "Many" } })).Status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed
            or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden);
        Assert.Null(await Tangent.Get("many", Cancellation));

        // No caller names a partition, and a list takes no word it does not declare.
        var partitioned = await Send(leo.Client, HttpMethod.Get, "/t?set=audit", json: true);
        Assert.Equal((HttpStatusCode.BadRequest, "invalid"), (partitioned.Status, partitioned.Kind));
        Assert.True(partitioned.Json.GetProperty("errors").TryGetProperty("set", out _), partitioned.Body);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(leo.Client, HttpMethod.Get, "/t?filter=%7B%7D", json: true)).Status);

        // A create writes its name, description and default kind, and nothing else of the row: not its slug, not its place.
        var placed = await Json(leo, "/t/new", "/t", new { name = "Placed", order = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, placed.Status);
        Assert.True(placed.Json.GetProperty("errors").TryGetProperty("order", out _), placed.Body);
        var slugged = await Json(leo, "/t/new", "/t", new { name = "Slugged", id = "elsewhere" });
        Assert.Equal(HttpStatusCode.BadRequest, slugged.Status);
        Assert.Null(await Tangent.Get("elsewhere", Cancellation));
        Assert.Null(await Tangent.Get("placed", Cancellation));

        // A change writes what it names and keeps the rest; it never writes the counts.
        var counted = await Json(leo, "/t/workshop/settings", HttpMethod.Patch, "/t/" + Workshop, new { topicCount = 9 });
        Assert.Equal(HttpStatusCode.BadRequest, counted.Status);
        Assert.Equal(0, (await Tangent.Get(Workshop, Cancellation))!.TopicCount);
    }

    [Fact]
    public async Task Joining_is_a_field_of_the_tangent_changed_on_both_doors_and_its_people_tab_sends_the_form_back_there()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var tools = await ToolClient.Connect(tangentSpace, leo);
        var people = "/t/" + Studio + "/settings/people";

        var called = await tools.Call("change_tangent", new { tangent = Studio, joining = "ask" });
        Assert.Equal((200, "studio"), (called.Status, called.Structured!.Value.GetProperty("slug").GetString()));
        Assert.Equal(Joining.Ask, (await Tangent.Get(Studio, Cancellation))!.Joining);

        // The people tab's form changes the Tangent at its own address and comes back to the people.
        var form = (await Send(leo.Client, HttpMethod.Get, people, json: true)).Json.GetProperty("changeJoining");
        Assert.Equal(("PATCH", BaseUrl + "/t/" + Studio), (form.GetProperty("method").GetString(), form.GetProperty("action").GetString()));
        var html = (await Send(leo.Client, HttpMethod.Get, people)).Body;
        Assert.Contains($"<input type=\"hidden\" name=\"{Render.Form.MethodField}\" value=\"PATCH\" />", html);
        Assert.Contains($"<input type=\"hidden\" name=\"{Render.Form.ReturnField}\" value=\"{people}\" />", html);
        var pressed = await Form(leo, people, "/t/" + Studio, new() { [Render.Form.MethodField] = "PATCH", [Render.Form.ReturnField] = people, ["joining"] = "join" });
        Assert.Equal((HttpStatusCode.SeeOther, people), (pressed.Status, pressed.Location));
        Assert.Equal(Joining.Join, (await Tangent.Get(Studio, Cancellation))!.Joining);

        // The moderators' Tangent keeps its joining.
        Assert.Equal(ModeratorsTangent.Slug, Assert.IsType<Outcome<Tangent>.Done>(await new CreateModeratorsTangent().Send(Cancellation)).Result.Id);
        var fixedJoining = await tools.Call("change_tangent", new { tangent = ModeratorsTangent.Slug, joining = "join" });
        Assert.Equal((400, Tangent.Fixed.Text), (fixedJoining.Status, fixedJoining.Structured!.Value.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task A_member_sent_as_null_stays_a_word_that_is_not_one_is_refused_by_its_field_and_the_settings_echo_back()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var tools = await ToolClient.Connect(tangentSpace, leo);
        var settings = "/t/" + Studio + "/settings";
        var before = (await Tangent.Get(Studio, Cancellation))!;

        // A change that sends a member as null leaves it as it stands, as JSON and as a tool.
        var nulled = await Json(leo, settings, HttpMethod.Patch, "/t/" + Studio,
            new { name = (string?)null, description = (string?)null, accent = (string?)null, image = (string?)null, kind = (string?)null, joining = (string?)null });
        Assert.Equal(HttpStatusCode.OK, nulled.Status);
        var called = await tools.Call("change_tangent", new { tangent = Studio, joining = "ask", name = (string?)null, description = (string?)null, accent = (string?)null });
        Assert.Equal((200, before.Name), (called.Status, called.Structured!.Value.GetProperty("name").GetString()));
        var after = (await Tangent.Get(Studio, Cancellation))!;
        Assert.Equal((before.Name, before.Description, before.Accent, before.Image, before.DefaultKind, Joining.Ask),
            (after.Name, after.Description, after.Accent, after.Image, after.DefaultKind, after.Joining));

        // A create reads a null description and a null kind as none, as JSON and as a tool, and a null name as no name.
        var created = await Json(leo, "/t/new", "/t", new { name = "Lanterns", description = (string?)null, kind = (string?)null });
        Assert.Equal((HttpStatusCode.Created, "", "topic"), (created.Status, created.Json.GetProperty("description").GetString(), created.Json.GetProperty("kind").GetString()));
        var tooled = await tools.Call("create_tangent", new { name = "Lamps", description = (string?)null });
        Assert.Equal((201, ""), (tooled.Status, tooled.Structured!.Value.GetProperty("description").GetString()));
        var unnamed = await Json(leo, "/t/new", "/t", new { name = (string?)null });
        Assert.Equal((HttpStatusCode.BadRequest, Tangent.NeedsName.Text), (unnamed.Status, unnamed.Json.GetProperty("detail").GetString()));
        Assert.True(unnamed.Json.GetProperty("errors").TryGetProperty(TangentWrites.NameField, out _), unnamed.Body);

        // A joining or a default kind that is not one of its words is refused naming its field, on both doors, and nothing changes.
        var open = await Json(leo, settings, HttpMethod.Patch, "/t/" + Studio, new { joining = "open" });
        Assert.Equal((HttpStatusCode.BadRequest, "invalid"), (open.Status, open.Kind));
        Assert.True(open.Json.GetProperty("errors").TryGetProperty(TangentWrites.JoiningField, out _), open.Body);
        var opened = await tools.Call("change_tangent", new { tangent = Studio, joining = "open" });
        Assert.Equal(400, opened.Status);
        Assert.True(opened.Structured!.Value.GetProperty("errors").TryGetProperty(TangentWrites.JoiningField, out _));
        var gallery = await tools.Call("change_tangent", new { tangent = Studio, kind = "gallery" });
        Assert.Equal((400, TangentWrites.KindRule.Text), (gallery.Status, gallery.Structured!.Value.GetProperty("detail").GetString()));
        Assert.True(gallery.Structured!.Value.GetProperty("errors").TryGetProperty(TangentWrites.KindField, out _));
        var galleryJson = await Json(leo, settings, HttpMethod.Patch, "/t/" + Studio, new { kind = "gallery" });
        Assert.Equal((HttpStatusCode.BadRequest, TangentWrites.KindRule.Text), (galleryJson.Status, galleryJson.Json.GetProperty("detail").GetString()));
        Assert.True(galleryJson.Json.GetProperty("errors").TryGetProperty(TangentWrites.KindField, out _), galleryJson.Body);
        Assert.Equal((Joining.Ask, before.DefaultKind), ((await Tangent.Get(Studio, Cancellation))!.Joining, (await Tangent.Get(Studio, Cancellation))!.DefaultKind));

        // The settings name the default kind as kind, as the change and the create take it, so an agent sends back what it read.
        var read = await tools.Call("read_tangent_settings", new { tangent = Studio });
        var kind = read.Structured!.Value.GetProperty("kind").GetString();
        var echoed = await tools.Call("change_tangent", new { tangent = Studio, kind = kind == "topic" ? "article" : "topic" });
        Assert.Equal((200, kind == "topic" ? "article" : "topic"), (echoed.Status, echoed.Structured!.Value.GetProperty("kind").GetString()));
        var opening = (await Send(leo.Client, HttpMethod.Get, "/t/new", json: true)).Json.GetProperty("kind").GetString();
        var made = await tools.Call("create_tangent", new { name = "Wicks", kind = opening });
        Assert.Equal((201, opening), (made.Status, made.Structured!.Value.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task A_members_only_tangent_that_may_be_joined_is_listed_to_whoever_takes_part_and_to_no_visitor()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        Assert.IsType<Outcome<Tangent>.Done>(await new ChangeTangentRow(Studio, Leo, row => row.Joining = Joining.Ask).Send(Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);
        using var visitor = Agent(tangentSpace);

        var listed = (await Send(ada.Client, HttpMethod.Get, "/t", json: true)).Json.GetProperty("tangents").EnumerateArray()
            .Single(tangent => tangent.GetProperty("slug").GetString() == Studio);
        Assert.Equal("ask", listed.GetProperty("joining").GetString());
        Assert.DoesNotContain(Studio, Slugs((await Send(visitor, HttpMethod.Get, "/t", json: true)).Json));

        // Kept out, she finds it no more.
        await tangentSpace.Services.GetRequiredService<RoleCollection>().Overwrite(Overwrites.Resource(Studio), OverwriteSubjectKind.Person, Ada, [], [Bits.Token(Bit.View)], ct: Cancellation);
        Assert.DoesNotContain(Studio, Slugs((await Send(ada.Client, HttpMethod.Get, "/t", json: true)).Json));
    }

    private static List<string?> Slugs(JsonElement page) => page.GetProperty("tangents").EnumerateArray().Select(tangent => tangent.GetProperty("slug").GetString()).ToList();
}
