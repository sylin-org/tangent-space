using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Tangents;

/// <summary>A Tangent's settings at <c>/t/{tangent}/settings</c>: offered to whoever holds ManageTangent there, or
/// ManageSpace; a rename keeps the slug; an audience change writes the everyone overwrite and appends tangent.changed,
/// and decides who reads; a move reorders the door; the moderators' Tangent keeps its audience and its place; the
/// settings answer JSON with <c>can</c> and <c>gate</c>; and whoever holds ManageSpace deletes a Tangent whole, past its
/// warning where it holds Topics, and nothing of it reaches anyone afterwards.</summary>
public sealed class TangentSettingsTests
{
    private static readonly string Leo = Persona.Id("leo4settings");

    /// <summary>The fields the settings form sends for the workshop as Writing's place holds it, with the changes given.</summary>
    private static Dictionary<string, string> Workshop(string? name = null, string? audience = null) => new()
    {
        ["name"] = name ?? "workshop", ["description"] = "", ["accent"] = "", ["image"] = "", ["kind"] = "topic", ["audience"] = audience ?? "public"
    };

    [Fact]
    public async Task Settings_show_only_for_whoever_holds_manage_tangent_there_and_anyone_else_is_refused_with_the_gate()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await roles.Overwrite(Overwrites.Resource("workshop"), OverwriteSubjectKind.Person, Bea, [Bits.Token(Bit.ManageTangent)], [], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        var settings = BaseUrl + "/t/workshop/settings";

        Assert.Equal(settings, (await Send(leo.Client, HttpMethod.Get, "/t/workshop", json: true)).Json.GetProperty("settings").GetString());
        Assert.Contains($"<a class=\"button tangent-settings\" href=\"{settings}\">Settings</a>", (await Send(leo.Client, HttpMethod.Get, "/t/workshop")).Body);
        Assert.Equal(settings, (await Send(bea.Client, HttpMethod.Get, "/t/workshop", json: true)).Json.GetProperty("settings").GetString());
        Assert.False((await Send(bea.Client, HttpMethod.Get, "/t/articles", json: true)).Json.TryGetProperty("settings", out _));
        Assert.False((await Send(ada.Client, HttpMethod.Get, "/t/workshop", json: true)).Json.TryGetProperty("settings", out _));
        Assert.DoesNotContain("tangent-settings", (await Send(ada.Client, HttpMethod.Get, "/t/workshop")).Body);

        var gate = Capabilities.NoRoleGrants(Bit.ManageTangent).Word;
        var offered = await Send(ada.Client, HttpMethod.Get, "/t/workshop/settings");
        Assert.Contains($"data-gate=\"{gate}\"", offered.Body);
        Assert.DoesNotContain("type=\"submit\"", PageContent(offered.Body));
        var refused = await Form(ada, "/t/workshop", "/t/workshop/settings", Workshop("Ada's workshop"));
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Contains($"data-gate=\"{gate}\"", refused.Body);
        Assert.Equal("workshop", (await Tangent.Get("workshop", Cancellation))!.Name);

        var renamed = await Form(bea, "/t/workshop", "/t/workshop/settings", Workshop("Bea's workshop"));
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/t/workshop"), (renamed.Status, renamed.Location));
        Assert.Equal("Bea's workshop", (await Tangent.Get("workshop", Cancellation))!.Name);
    }

    [Fact]
    public async Task A_rename_keeps_the_slug_and_appends_tangent_changed_and_a_change_of_nothing_writes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        var before = await Event.Count.Exact(Cancellation);

        var renamed = await Form(leo, "/t/workshop/settings", "/t/workshop/settings", new()
        {
            ["name"] = "The big workshop", ["description"] = "Where things get made.", ["kind"] = "article", ["audience"] = "public"
        });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/t/workshop"), (renamed.Status, renamed.Location));
        var row = (await Tangent.Get("workshop", Cancellation))!;
        Assert.Equal(("workshop", "The big workshop", "Where things get made.", PostKind.Article), (row.Id, row.Name, row.Description, row.DefaultKind));
        Assert.Null(await Tangent.Get("the-big-workshop", Cancellation));
        var logged = Assert.Single(await Event.All(Cancellation), e => e.Seq > before);
        Assert.Equal((EventKind.TangentChanged, "workshop", Leo, Audience.Public), (logged.Kind, logged.TangentId, logged.ActorId, logged.Audience));
        var page = (await Send(leo.Client, HttpMethod.Get, "/t/workshop", json: true)).Json;
        Assert.Equal(("workshop", "The big workshop", "article"), (page.GetProperty("slug").GetString(), page.GetProperty("name").GetString(),
            page.GetProperty("kind").GetString()));

        var again = await Form(leo, "/t/workshop/settings", "/t/workshop/settings", new()
        {
            ["name"] = "The big workshop", ["description"] = "Where things get made.", ["kind"] = "article", ["audience"] = "public"
        });
        Assert.Equal(HttpStatusCode.SeeOther, again.Status);
        Assert.Equal(before + 1, await Event.Count.Exact(Cancellation));

        var tooLong = new string('w', Tangent.NameLimit + 1);
        var refused = await Form(leo, "/t/workshop/settings", "/t/workshop/settings", Workshop(tooLong));
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Contains($"value=\"{tooLong}\"", refused.Body);
        Assert.Contains("id=\"settings-name-failure\"", refused.Body);
        Assert.Equal("The big workshop", (await Tangent.Get("workshop", Cancellation))!.Name);
    }

    [Fact]
    public async Task An_audience_change_writes_the_everyone_overwrite_appends_tangent_changed_and_decides_whether_a_member_reads()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        Assert.Equal(HttpStatusCode.OK, (await Send(ada.Client, HttpMethod.Get, "/t/workshop", json: true)).Status);

        var closed = await Form(leo, "/t/workshop/settings", "/t/workshop/settings", Workshop(audience: "private"));

        Assert.Equal(HttpStatusCode.SeeOther, closed.Status);
        Assert.Equal(Audience.Private, await Overwrites.AudienceOf("workshop", Cancellation));
        var everyone = Assert.Single((await roles.Overwrites(Overwrites.Resource("workshop"), ct: Cancellation)).Items);
        Assert.Empty(everyone.Allow);
        Assert.Equal([Bits.Token(Bit.View)], everyone.Deny);
        var logged = (await Event.All(Cancellation)).OrderBy(e => e.Seq).Last();
        Assert.Equal((EventKind.TangentChanged, "workshop", Audience.Private), (logged.Kind, logged.TangentId, logged.Audience));
        var absent = await Send(ada.Client, HttpMethod.Get, "/t/workshop", json: true);
        Assert.Equal((HttpStatusCode.NotFound, "absent"), (absent.Status, absent.Kind));
        Assert.Equal(HttpStatusCode.OK, (await Send(leo.Client, HttpMethod.Get, "/t/workshop", json: true)).Status);

        Assert.Equal(HttpStatusCode.SeeOther, (await Form(leo, "/t/workshop/settings", "/t/workshop/settings", Workshop(audience: "public"))).Status);

        Assert.Equal(Audience.Public, await Overwrites.AudienceOf("workshop", Cancellation));
        Assert.Equal(Audience.Public, (await Event.All(Cancellation)).OrderBy(e => e.Seq).Last().Audience);
        Assert.Equal(HttpStatusCode.OK, (await Send(ada.Client, HttpMethod.Get, "/t/workshop", json: true)).Status);
    }

    [Fact]
    public async Task A_move_reorders_the_doors_list_one_place_at_a_time()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        Assert.Equal(["studio", "workshop", "articles"], await DoorOrder(leo));

        var moved = await Form(leo, "/t/articles/settings", "/t/articles/settings", new() { ["move"] = "earlier" });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/t/articles/settings"), (moved.Status, moved.Location));
        Assert.Equal(["studio", "articles", "workshop"], await DoorOrder(leo));
        Assert.Equal((1, 2, 3), ((await Tangent.Get("studio", Cancellation))!.Order, (await Tangent.Get("articles", Cancellation))!.Order,
            (await Tangent.Get("workshop", Cancellation))!.Order));
        var settings = (await Send(leo.Client, HttpMethod.Get, "/t/articles/settings", json: true)).Json;
        Assert.Equal(("studio", "workshop"), (settings.GetProperty("before").GetString(), settings.GetProperty("after").GetString()));
        Assert.Contains("It comes after studio and before workshop.", WebUtility.HtmlDecode((await Send(leo.Client, HttpMethod.Get, "/t/articles/settings")).Body));

        Assert.Equal(HttpStatusCode.SeeOther, (await Form(leo, "/t/studio/settings", "/t/studio/settings", new() { ["move"] = "later" })).Status);
        Assert.Equal(["articles", "studio", "workshop"], await DoorOrder(leo));
        var first = await Form(leo, "/t/articles/settings", "/t/articles/settings", new() { ["move"] = "earlier" });
        Assert.Equal(HttpStatusCode.SeeOther, first.Status);
        Assert.Equal(["articles", "studio", "workshop"], await DoorOrder(leo));
    }

    [Fact]
    public async Task The_moderators_tangent_is_renamed_and_described_but_its_audience_and_place_are_never_changed()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        Assert.Equal(ModeratorsTangent.Slug, Assert.IsType<Outcome<Tangent>.Done>(await new CreateModeratorsTangent().Send(Cancellation)).Result.Id);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var ada = await SignedIn(tangentSpace, Ada);
        const string Path = "/t/moderators/settings";

        var settings = (await Send(leo.Client, HttpMethod.Get, Path, json: true)).Json;
        Assert.True(settings.GetProperty("fixed").GetBoolean());
        Assert.Equal("private", settings.GetProperty("audience").GetString());
        Assert.Empty(settings.GetProperty("audiences").EnumerateArray());
        var page = (await Send(leo.Client, HttpMethod.Get, Path)).Body;
        Assert.DoesNotContain("name=\"audience\"", page);
        Assert.DoesNotContain("Move earlier", page);

        var opened = await Form(leo, Path, Path, new() { ["name"] = "Moderation", ["description"] = "Reports and talk.", ["kind"] = "topic", ["audience"] = "public" });
        Assert.Equal(HttpStatusCode.BadRequest, opened.Status);
        Assert.Contains(ChangeTangent.Fixed.Text, WebUtility.HtmlDecode(opened.Body));
        var asJson = await Json(leo, Path, Path, new { audience = "public" });
        Assert.Equal((HttpStatusCode.BadRequest, ChangeTangent.Fixed.Text), (asJson.Status, asJson.Json.GetProperty("detail").GetString()));
        var moved = await Json(leo, Path, Path, new { move = "earlier" });
        Assert.Equal((HttpStatusCode.BadRequest, ChangeTangent.Fixed.Text), (moved.Status, moved.Json.GetProperty("detail").GetString()));
        Assert.Equal(Audience.Private, await Overwrites.AudienceOf(ModeratorsTangent.Slug, Cancellation));
        Assert.Equal(ModeratorsTangent.Name, (await Tangent.Get(ModeratorsTangent.Slug, Cancellation))!.Name);

        var renamed = await Form(leo, Path, Path, new() { ["name"] = "Moderation", ["description"] = "Reports and talk.", ["kind"] = "topic" });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/t/moderators"), (renamed.Status, renamed.Location));
        var row = (await Tangent.Get(ModeratorsTangent.Slug, Cancellation))!;
        Assert.Equal(("Moderation", "Reports and talk.", ModeratorsTangent.Order), (row.Name, row.Description, row.Order));
        Assert.Equal(Audience.Private, (await Event.All(Cancellation)).OrderBy(e => e.Seq).Last().Audience);
        Assert.Equal(HttpStatusCode.OK, (await Send(mo.Client, HttpMethod.Get, "/t/moderators", json: true)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/t/moderators", json: true)).Status);
        Assert.Equal("moderators", (await DoorOrder(leo)).Last());
    }

    [Fact]
    public async Task The_settings_answer_json_with_can_and_gate_and_change_by_json_only_what_is_sent()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);

        var settings = (await Send(leo.Client, HttpMethod.Get, "/t/workshop/settings", json: true)).Json;
        Assert.Equal(("workshop", "workshop", "topic", "public", BaseUrl + "/t/workshop/settings", false),
            (settings.GetProperty("slug").GetString(), settings.GetProperty("name").GetString(), settings.GetProperty("kind").GetString(),
                settings.GetProperty("audience").GetString(), settings.GetProperty("change").GetProperty("action").GetString(), settings.GetProperty("fixed").GetBoolean()));
        Assert.Equal(["name", "description", "accent", "image", "kind", "audience", "move"], FieldNames(settings.GetProperty("change")));
        Assert.Contains("ManageTangent", Strings(settings.GetProperty("can")));
        Assert.False(settings.TryGetProperty("gate", out _));
        var member = (await Send(ada.Client, HttpMethod.Get, "/t/workshop/settings", json: true)).Json;
        Assert.Equal(Capabilities.NoRoleGrants(Bit.ManageTangent).Word, member.GetProperty("gate").GetString());
        Assert.Equal(["View", "Post", "Topic", "EditOwn", "RemoveOwn"], Strings(member.GetProperty("can")));
        Assert.False(member.TryGetProperty("change", out _));
        var studio = await Send(ada.Client, HttpMethod.Get, "/t/studio/settings", json: true);
        Assert.Equal((HttpStatusCode.NotFound, "absent"), (studio.Status, studio.Kind));

        var described = await Json(leo, "/t/workshop/settings", "/t/workshop/settings", new { description = "Only this." });

        Assert.Equal((HttpStatusCode.OK, BaseUrl + "/t/workshop"), (described.Status, described.Location));
        Assert.Equal(("workshop", "Only this.", "public"), (described.Json.GetProperty("name").GetString(),
            described.Json.GetProperty("description").GetString(), described.Json.GetProperty("audience").GetString()));
        Assert.Contains("ManageTangent", Strings(described.Json.GetProperty("can")));
        var refused = await Json(ada, "/t/workshop", "/t/workshop/settings", new { description = "Mine." });
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageTangent).Word), (refused.Status, refused.Word));
    }

    [Fact]
    public async Task The_settings_say_where_a_tangent_stands_only_to_whoever_may_move_it_and_name_only_tangents_they_may_view()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await roles.Overwrite(Overwrites.Resource("workshop"), OverwriteSubjectKind.Person, Bea, [Bits.Token(Bit.ManageTangent)], [], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);

        foreach (var reader in new[] { ada.Client, bea.Client, Agent(tangentSpace) })
        {
            var json = (await Send(reader, HttpMethod.Get, "/t/workshop/settings", json: true)).Json;
            Assert.False(json.GetProperty("movable").GetBoolean());
            Assert.False(json.TryGetProperty("before", out _));
            Assert.False(json.TryGetProperty("after", out _));
            var html = WebUtility.HtmlDecode((await Send(reader, HttpMethod.Get, "/t/workshop/settings")).Body);
            Assert.DoesNotContain("studio", html);
            Assert.DoesNotContain("Place among the Tangents", html);
            Assert.DoesNotContain("Move earlier", html);
        }

        var administrator = (await Send(leo.Client, HttpMethod.Get, "/t/workshop/settings", json: true)).Json;
        Assert.True(administrator.GetProperty("movable").GetBoolean());
        Assert.Equal(("studio", "Articles"), (administrator.GetProperty("before").GetString(), administrator.GetProperty("after").GetString()));
        var moved = await Json(bea, "/t/workshop", "/t/workshop/settings", new { move = "earlier" });
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageSpace).Word), (moved.Status, moved.Word));
        Assert.Equal(["studio", "workshop", "articles"], await DoorOrder(leo));
    }

    [Fact]
    public async Task A_change_that_closes_the_tangent_to_its_own_sender_is_recorded_once_and_lands_them_on_a_page_that_says_so()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await roles.Overwrite(Overwrites.Resource("workshop"), OverwriteSubjectKind.Person, Bea, [Bits.Token(Bit.ManageTangent)], [], ct: Cancellation);
        using var bea = await SignedIn(tangentSpace, Bea);
        var before = await Event.Count.Exact(Cancellation);

        var closed = await Form(bea, "/t/workshop/settings", "/t/workshop/settings", Workshop(audience: "private"));

        Assert.Equal(HttpStatusCode.OK, closed.Status);
        Assert.Contains("workshop is private now, and no longer shows for you.", WebUtility.HtmlDecode(closed.Body));
        Assert.Contains($"href=\"{BaseUrl}/\">Go to the door</a>", closed.Body);
        Assert.Equal(Audience.Private, await Overwrites.AudienceOf("workshop", Cancellation));
        var logged = Assert.Single(await Event.All(Cancellation), e => e.Seq > before);
        Assert.Equal((EventKind.TangentChanged, "workshop", Bea, Audience.Private), (logged.Kind, logged.TangentId, logged.ActorId, logged.Audience));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(bea.Client, HttpMethod.Get, "/t/workshop", json: true)).Status);
    }

    [Fact]
    public async Task A_json_change_gives_the_order_only_to_whoever_arranges_the_list_and_a_change_that_closes_the_tangent_says_only_that_it_saved()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await roles.Overwrite(Overwrites.Resource("workshop"), OverwriteSubjectKind.Person, Bea, [Bits.Token(Bit.ManageTangent)], [], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var bea = await SignedIn(tangentSpace, Bea);

        var described = await Json(bea, "/t/workshop", "/t/workshop/settings", new { description = "x" });
        Assert.Equal(HttpStatusCode.OK, described.Status);
        Assert.Equal("x", described.Json.GetProperty("description").GetString());
        Assert.False(described.Json.TryGetProperty("order", out _));
        var arranged = await Json(leo, "/t/workshop/settings", "/t/workshop/settings", new { description = "y" });
        Assert.Equal(1, arranged.Json.GetProperty("order").GetInt32());

        var closed = await Json(bea, "/t/workshop", "/t/workshop/settings", new { audience = "private" });

        Assert.Equal(HttpStatusCode.OK, closed.Status);
        Assert.Equal(("workshop is private now, and no longer shows for you.", BaseUrl + "/"),
            (closed.Json.GetProperty("saved").GetString(), closed.Json.GetProperty("door").GetString()));
        foreach (var property in new[] { "slug", "description", "audience", "order", "address", "settings", "can" })
            Assert.False(closed.Json.TryGetProperty(property, out _), property);
        Assert.Equal(Audience.Private, await Overwrites.AudienceOf("workshop", Cancellation));
    }

    [Fact]
    public async Task A_manager_deletes_an_empty_tangent_at_one_press_and_one_with_topics_past_its_warning_and_the_moderators_tangent_stays()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangent>.Done>(await new CreateModeratorsTangent().Send(Cancellation));
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await roles.Overwrite(Overwrites.Resource("workshop"), OverwriteSubjectKind.Person, Bea, [Bits.Token(Bit.ManageTangent)], [], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);

        // An empty Tangent goes at one press, with nothing to warn of.
        Assert.Equal(HttpStatusCode.Created, (await Json(leo, "/t/new", "/t/new", new { name = "Corner" })).Status);
        var corner = PageContent((await Send(leo.Client, HttpMethod.Get, "/t/corner/settings")).Body);
        Assert.Contains("Delete this Tangent", corner);
        Assert.DoesNotContain("post-remove-note", corner);
        var pressed = await Form(leo, "/t/corner/settings", "/t/corner/settings/delete", new() { ["topics"] = "0" });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/t"), (pressed.Status, pressed.Location));
        Assert.Null(await Tangent.Get("corner", Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, (await Form(leo, "/t/corner/settings", "/t/corner/settings/delete", new() { ["topics"] = "0" })).Status);

        // A private Tangent with a Topic, an edited reply, an image, an ask and a person let in.
        var image = await Media.Keep(ArticleMediaTests.Jpeg(), Cancellation);
        Assert.IsType<Outcome<MediaUse>.Done>(await new BringMedia(image.Id, Leo).Send(Cancellation));
        var root = await Started(Leo, "Plans", "![A plan](/media/" + image.Id + ")", Studio);
        var reply = await Replied(root, Leo, "First draft.");
        Assert.IsType<Outcome<Post>.Done>(await new EditPost(reply.Id, 1, PostBody.Of("Second draft."), null, Leo).Send(Cancellation));
        Assert.IsType<Outcome<Tangent>.Done>(await new ChangeTangent(Studio, Leo, Image: "/media/" + image.Id).Send(Cancellation));
        await Inside.LetIn(roles, Studio, Bea, Cancellation);
        Assert.IsType<Outcome<Ask>.Done>(await new Store<Ask>(new Ask { Id = Ask.Key(Studio, Ada), TangentId = Studio, PersonId = Ada, AskedAt = DateTimeOffset.UtcNow }).Send(Cancellation));
        Assert.NotNull(await AuditPartition.At<Post>(reply.Id, 1, Cancellation));
        Assert.NotNull(await MediaUse.Get(MediaUse.Key(image.Id, root.Id), Cancellation));
        Assert.NotNull(await MediaUse.Get(MediaUse.Key(image.Id, "tangent:" + Studio), Cancellation));

        Assert.Contains(await Polled(bea), row => row.Tangent == Studio);

        var warned = WebUtility.HtmlDecode((await Send(leo.Client, HttpMethod.Get, "/t/studio/settings")).Body);
        Assert.Contains("Its one topic and every reply in it go for good, and no tombstone stays in its place.", warned);
        Assert.Contains($"href=\"{BaseUrl}/settings/export/studio.zip\">Download it first</a> to keep a copy.", warned);
        var settings = (await Send(leo.Client, HttpMethod.Get, "/t/studio/settings", json: true)).Json;
        Assert.Equal((1, 0, "1"), (settings.GetProperty("topics").GetInt32(), settings.GetProperty("onBluesky").GetInt32(), Valued(settings.GetProperty("delete"), "topics")));

        var bare = await Json(leo, "/t/studio/settings", "/t/studio/settings/delete", new { });
        Assert.Equal((HttpStatusCode.Conflict, DeleteTangent.HoldsTopicsWord), (bare.Status, bare.Word));
        Assert.Equal(HttpStatusCode.Conflict, (await Json(leo, "/t/studio/settings", "/t/studio/settings/delete", new { topics = 1000 })).Status);
        var stale = await Form(leo, "/t/studio/settings", "/t/studio/settings/delete", new() { ["topics"] = "0" });
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Contains(DeleteTangent.HoldsTopics(1).Text, WebUtility.HtmlDecode(stale.Body));
        Assert.NotNull(await Tangent.Get(Studio, Cancellation));
        Assert.NotNull(await Post.Get(root.Id, Cancellation));

        var deleted = await Json(leo, "/t/studio/settings", "/t/studio/settings/delete", new { topics = 1 });

        Assert.Equal((HttpStatusCode.OK, Studio, 1), (deleted.Status, deleted.Json.GetProperty("slug").GetString(), deleted.Json.GetProperty("topics").GetInt32()));
        Assert.Null(await Tangent.Get(Studio, Cancellation));
        Assert.Null(await Post.Get(root.Id, Cancellation));
        Assert.Null(await Post.Get(reply.Id, Cancellation));
        Assert.Null(await AuditPartition.At<Post>(reply.Id, 1, Cancellation));
        Assert.Null(await MediaUse.Get(MediaUse.Key(image.Id, root.Id), Cancellation));
        Assert.Null(await MediaUse.Get(MediaUse.Key(image.Id, "tangent:" + Studio), Cancellation));
        Assert.Null(await Ask.Get(Ask.Key(Studio, Ada), Cancellation));
        Assert.Empty((await roles.Overwrites(Overwrites.Resource(Studio), ct: Cancellation)).Items);
        var logged = (await Event.All(Cancellation)).OrderBy(e => e.Seq).Last();
        Assert.Equal((EventKind.TangentDeleted, Studio, Leo), (logged.Kind, logged.TangentId, logged.ActorId));
        Assert.DoesNotContain(await Polled(bea), row => row.Tangent == Studio);

        // The slug taken again starts clean: none of the deleted Tangent's rows reaches the new one's readers.
        Assert.Equal(HttpStatusCode.Created, (await Json(leo, "/t/new", "/t/new", new { name = Studio })).Status);
        var again = (await Tangent.Get(Studio, Cancellation))!;
        var reached = (await Polled(bea)).Where(row => row.Tangent == Studio).ToList();
        Assert.Contains(reached, row => row.Seq == again.LogSeq);
        Assert.All(reached, row => Assert.True(row.Seq >= again.LogSeq));
        Assert.DoesNotContain(Leo, (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("around").GetProperty("people")
            .EnumerateArray().Select(person => person.GetProperty("id").GetString()));

        // The moderators' Tangent stays, and whoever manages a Tangent without ManageSpace is offered no delete.
        Assert.False((await Send(leo.Client, HttpMethod.Get, "/t/moderators/settings", json: true)).Json.TryGetProperty("delete", out _));
        var kept = await Json(leo, "/t/moderators/settings", "/t/moderators/settings/delete", new { topics = 0 });
        Assert.Equal((HttpStatusCode.BadRequest, DeleteTangent.Kept.Text), (kept.Status, kept.Json.GetProperty("detail").GetString()));
        Assert.False((await Send(bea.Client, HttpMethod.Get, "/t/workshop/settings", json: true)).Json.TryGetProperty("delete", out _));
        var gated = await Json(bea, "/t/workshop/settings", "/t/workshop/settings/delete", new { topics = 0 });
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageSpace).Word), (gated.Status, gated.Word));
        Assert.NotNull(await Tangent.Get("workshop", Cancellation));
    }

    /// <summary>What a poll from the start of the log brings the reader: the rows that reach them, as the poll says them.</summary>
    private static async Task<List<(long Seq, string? Tangent)>> Polled(Participants.Browser reader)
        => (await Send(reader.Client, HttpMethod.Get, EventsController.Path + "?since=0", json: true)).Json.GetProperty("events").EnumerateArray()
            .Select(row => (row.GetProperty("seq").GetInt64(), row.TryGetProperty("tangent", out var tangent) ? tangent.GetString() : null)).ToList();

    /// <summary>The slugs of the Tangents the door lists for the reader, in its order.</summary>
    private static async Task<List<string?>> DoorOrder(Participants.Browser reader)
        => (await Send(reader.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("tangents").EnumerateArray()
            .Select(tangent => tangent.GetProperty("slug").GetString()).ToList();

    private static List<string?> Strings(JsonElement values) => values.EnumerateArray().Select(value => value.GetString()).ToList();
}
