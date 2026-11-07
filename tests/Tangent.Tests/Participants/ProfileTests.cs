using System.Net;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Koan.Web.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Participants;

/// <summary>A participant's profile at <c>/p/{handle}</c>: the profile their PDS gave, their roles, when they arrived
/// and their recent Posts in the Tangents the reader may view, both ways, with a cursor; absent where the place or the
/// handle is; names on Posts and the door linking to it; and, on the reader's own profile alone, the agents they
/// approved, each revoked in the page through Koan.</summary>
public sealed class ProfileTests
{
    private static readonly string Leo = Persona.Id("leo4profile");
    private static readonly string Ann = Persona.Id("ann4profile");
    private const string LeoHandle = "leo4profile.example";
    private const string AdaHandle = "ada4writes.example";

    [Fact]
    public async Task A_profile_shows_the_pds_profile_roles_arrival_and_recent_posts_and_leaves_out_a_tangent_the_reader_may_not_view()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ann, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var ann = await SignedIn(tangentSpace, Ann);
        Assert.IsType<Outcome<Participant>.Done>(await new CaptureProfile(Leo, 
            new ProfileAnswer(Persona.Did(Leo), "Leo Botinelly", "Builds places to talk.", "https://cdn.example/leo.jpg"), DateTimeOffset.UtcNow).Send(Cancellation));

        var open = await Started(Leo, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(open, Leo, "I do, at six.");
        var secret = await Started(Leo, "The kiln budget", "Numbers for the studio.", Studio);
        var removed = await Started(Leo, "A draft", "Gone soon.");
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(removed.Id, Leo).Send(Cancellation));

        var asAda = await Send(ada.Client, HttpMethod.Get, "/p/" + LeoHandle, json: true);
        Assert.Equal(HttpStatusCode.OK, asAda.Status);
        var profile = asAda.Json;
        Assert.Equal((Leo, LeoHandle, "Leo Botinelly", "Leo Botinelly", "Builds places to talk.", "https://cdn.example/leo.jpg", "person", BaseUrl + "/p/" + LeoHandle),
            (profile.GetProperty("id").GetString(), profile.GetProperty("handle").GetString(), profile.GetProperty("name").GetString(),
                profile.GetProperty("displayName").GetString(), profile.GetProperty("description").GetString(), profile.GetProperty("avatar").GetString(),
                profile.GetProperty("kind").GetString(), profile.GetProperty("address").GetString()));
        // Signing in joined Leo to the member role besides the administrator role he was given.
        Assert.Equal(["Administrator", "Member"], Strings(profile.GetProperty("roles")));
        Assert.NotEqual(JsonValueKind.Null, profile.GetProperty("arrived").ValueKind);
        // Newest first, the studio's Topic and the removed one absent, nothing counted.
        Assert.Equal([reply.Id, open.Id], Ids(profile));
        Assert.Equal(["reply", "topic"], profile.GetProperty("posts").EnumerateArray().Select(post => post.GetProperty("kind").GetString()!).ToList());
        Assert.Equal(AddressOf(open, reply), profile.GetProperty("posts")[0].GetProperty("address").GetString());
        Assert.Equal("Lanterns at dusk", profile.GetProperty("posts")[0].GetProperty("topic").GetString());
        Assert.DoesNotContain("count", profile.EnumerateObject().Select(member => member.Name), StringComparer.OrdinalIgnoreCase);

        var html = await Send(ada.Client, HttpMethod.Get, "/p/" + LeoHandle);
        Assert.Contains("Builds places to talk.", html.Body);
        Assert.Contains("<span class=\"profile-roles\">Administrator, Member</span>", html.Body);
        Assert.Contains("Lanterns at dusk", html.Body);
        Assert.DoesNotContain("The kiln budget", html.Body);
        Assert.DoesNotContain(secret.Id, html.Body);

        // An administrator may view the studio, so its Topic is among Leo's posts for them.
        var asAnn = await Send(ann.Client, HttpMethod.Get, "/p/" + LeoHandle, json: true);
        Assert.Equal([secret.Id, reply.Id, open.Id], Ids(asAnn.Json));

        // The DID answers for the same participant.
        var byDid = await Send(ada.Client, HttpMethod.Get, "/p/" + Uri.EscapeDataString(Leo), json: true);
        Assert.Equal((HttpStatusCode.OK, LeoHandle), (byDid.Status, byDid.Json.GetProperty("handle").GetString()));
    }

    [Fact]
    public async Task A_visitor_reads_a_profile_on_a_public_place_and_on_a_private_one_reads_its_gate_whatever_the_handle()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        (await SignedIn(tangentSpace, Ada)).Dispose();
        await Started(Ada, "Wicks", "Cotton or hemp?");
        using var visitor = Agent(tangentSpace);

        var page = await Send(visitor, HttpMethod.Get, "/p/" + AdaHandle);
        var json = await Send(visitor, HttpMethod.Get, "/p/" + AdaHandle, json: true);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Wicks", page.Body);
        Assert.Equal((Capabilities.SignIn.Word, BaseUrl + "/sign-in?return=%2Fp%2F" + AdaHandle),
            (json.Json.GetProperty("gate").GetString(), json.Json.GetProperty("signIn").GetString()));
        Assert.DoesNotContain("Post", Strings(json.Json.GetProperty("can")));

        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Audience = Audience.Private;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));

        var held = await Send(visitor, HttpMethod.Get, "/p/" + AdaHandle, json: true);
        var unheld = await Send(visitor, HttpMethod.Get, "/p/nobody.example", json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word), (held.Status, held.Word));
        Assert.Equal((held.Status, held.Title, held.Word), (unheld.Status, unheld.Title, unheld.Word));
        Assert.DoesNotContain("Wicks", (await Send(visitor, HttpMethod.Get, "/p/" + AdaHandle)).Body);
    }

    [Fact]
    public async Task A_handle_no_participant_holds_now_is_absent_and_a_changed_handle_answers_at_the_new_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var visitor = Agent(tangentSpace);

        var unknown = await Send(visitor, HttpMethod.Get, "/p/nobody.example", json: true);
        Assert.Equal((HttpStatusCode.NotFound, "absent"), (unknown.Status, unknown.Kind));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/p/did%3Aplc%3Anobody")).Status);

        // Ada signs in again under a new handle: the row keeps the new one, and the old one is no one's here.
        (await ada.SignIn("ada", Ada, Cancellation, handle: "ada.example")).Dispose();
        Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, "/p/" + AdaHandle)).Status);
        var moved = await Send(visitor, HttpMethod.Get, "/p/ada.example", json: true);
        Assert.Equal((HttpStatusCode.OK, Ada), (moved.Status, moved.Json.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/p/ADA.example")).Status);
    }

    [Fact]
    public async Task A_profile_answers_at_its_handle_its_id_and_its_did_alike_on_both_doors_and_takes_nothing_else()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var tools = await Tools.ToolClient.Connect(tangentSpace, ada);

        foreach (var address in new[] { AdaHandle, Ada, Persona.Did(Ada) })
        {
            var read = await Send(ada.Client, HttpMethod.Get, "/p/" + Uri.EscapeDataString(address), json: true);
            Assert.Equal((HttpStatusCode.OK, Ada, BaseUrl + "/p/" + AdaHandle),
                (read.Status, read.Json.GetProperty("id").GetString(), read.Json.GetProperty("address").GetString()));
            var page = await Send(ada.Client, HttpMethod.Get, "/p/" + Uri.EscapeDataString(address));
            Assert.Contains("class=\"profile", page.Body);
            var called = await tools.Call("read_profile", new { handle = address });
            Assert.Equal((200, Ada), (called.Status, called.Structured!.Value.GetProperty("id").GetString()));
        }

        // The profile reads its row alone: no partition, and no word it does not take, on either door.
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ada.Client, HttpMethod.Get, "/p/" + AdaHandle + "?set=audit", json: true)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ada.Client, HttpMethod.Get, "/p/" + AdaHandle + "?filter=%7B%7D", json: true)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/p", json: true)).Status);
        Assert.Equal(400, (await tools.Call("read_profile", new { handle = AdaHandle, before = "yesterday" })).Status);
        var absent = await tools.Call("read_profile", new { handle = "nobody.example" });
        Assert.Equal((404, "absent"), (absent.Status, absent.ErrorCode));
    }

    [Fact]
    public async Task A_handle_verified_at_a_sign_in_is_taken_from_the_participant_who_kept_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = new Browser(tangentSpace);
        (await ada.SignIn("ada", Ada, Cancellation, handle: "x.example")).Dispose();
        using var bea = new Browser(tangentSpace);
        using var visitor = Agent(tangentSpace);
        Assert.Equal(Ada, (await Send(visitor, HttpMethod.Get, "/p/x.example", json: true)).Json.GetProperty("id").GetString());

        // Ada gave x.example up and has not signed in since; Bea signs in verified as x.example.
        (await bea.SignIn("bea", Bea, Cancellation, handle: "x.example")).Dispose();

        Assert.Equal(Bea, (await Send(visitor, HttpMethod.Get, "/p/x.example", json: true)).Json.GetProperty("id").GetString());
        Assert.Equal("", (await Participant.Get(Ada, Cancellation))!.Handle);
        var byDid = await Send(visitor, HttpMethod.Get, "/p/" + Uri.EscapeDataString(Persona.Did(Ada)), json: true);
        Assert.Equal((HttpStatusCode.OK, Persona.Did(Ada), BaseUrl + "/p/" + Uri.EscapeDataString(Persona.Did(Ada))),
            (byDid.Status, byDid.Json.GetProperty("handle").GetString(), byDid.Json.GetProperty("address").GetString()));
    }

    [Fact]
    public async Task Names_on_posts_and_on_the_door_link_to_their_profiles()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        (await SignedIn(tangentSpace, Ada)).Dispose();
        var root = await Started(Ada, "Wicks", "Cotton or hemp?");
        using var visitor = Agent(tangentSpace);

        var topic = await Send(visitor, HttpMethod.Get, TopicPath(root));
        var door = await Send(visitor, HttpMethod.Get, "/");
        var doorJson = await Send(visitor, HttpMethod.Get, "/", json: true);

        Assert.Contains($"<a class=\"participant-name\" href=\"{BaseUrl}/p/{AdaHandle}\" data-account=\"{Persona.Did(Ada)}\">", topic.Body);
        Assert.Contains($"<a class=\"participant-name\" href=\"{BaseUrl}/p/{AdaHandle}\">", door.Body);
        Assert.Equal(BaseUrl + "/p/" + AdaHandle, doorJson.Json.GetProperty("around").GetProperty("people")[0].GetProperty("profile").GetString());
    }

    [Fact]
    public async Task The_json_carries_can_gate_and_a_cursor_through_the_posts()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var written = new List<string>();
        for (var index = 0; index < RecentPosts.PageSize + 5; index++)
            written.Add((await Started(Ada, "Topic " + index, "Body " + index)).Id);
        written.Reverse();

        var first = (await Send(ada.Client, HttpMethod.Get, "/p/" + AdaHandle, json: true)).Json;
        Assert.Contains("Post", Strings(first.GetProperty("can")));
        Assert.True(Missing(first, "gate"));
        Assert.Equal(written.Take(RecentPosts.PageSize), Ids(first));
        Assert.False(first.GetProperty("continued").GetBoolean());
        var older = new Uri(first.GetProperty("older").GetString()!);

        var second = (await Send(ada.Client, HttpMethod.Get, older.PathAndQuery, json: true)).Json;
        Assert.Equal(written.Skip(RecentPosts.PageSize), Ids(second));
        Assert.True(second.GetProperty("continued").GetBoolean());
        Assert.True(Missing(second, "older"));

        var refused = await Send(ada.Client, HttpMethod.Get, "/p/" + AdaHandle + "?before=yesterday", json: true);
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
    }

    /// <summary>A page reads only the Tangents the reader may view: however many Posts lie in another, more than its bound of
    /// reads would read past, each read after the first leaves that Tangent out, so a page is full of those the reader may
    /// view, and neither its length nor its cursor says anything of the rest.</summary>
    [Fact]
    public async Task Posts_in_a_tangent_the_reader_may_not_view_leave_no_trace_on_a_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var leo = await SignedIn(tangentSpace, Leo);
        var older = new List<string>();
        for (var index = 0; index < 2; index++) older.Add((await Started(Leo, "Older " + index, "Body.")).Id);
        var hidden = new List<string>();
        for (var index = 0; index < Tangents.ViewableTangents.Met.ReadLimit * (RecentPosts.PageSize + 1); index++) hidden.Add((await Started(Leo, "Studio " + index, "Body.", Studio)).Id);
        var newer = new List<string>();
        for (var index = 0; index < 3; index++) newer.Add((await Started(Leo, "Newer " + index, "Body.")).Id);
        older.Reverse();
        newer.Reverse();

        var asAda = await Send(ada.Client, HttpMethod.Get, "/p/" + LeoHandle, json: true);
        Assert.Equal([.. newer, .. older], Ids(asAda.Json));
        Assert.True(Missing(asAda.Json, "older"));
        Assert.DoesNotContain(hidden, id => asAda.Body.Contains(id, StringComparison.Ordinal));

        // An administrator may view the studio: their first page is full, and continues from a Post they may view.
        var asLeo = (await Send(leo.Client, HttpMethod.Get, "/p/" + LeoHandle, json: true)).Json;
        Assert.Equal(RecentPosts.PageSize, Ids(asLeo).Count);
        Assert.Equal(newer, Ids(asLeo).Take(3));
        Assert.Contains(Ids(asLeo)[^1], asLeo.GetProperty("older").GetString()!);
    }

    /// <summary>A page reads past the Posts its reader may not view, holds a page of those they may, and continues from the
    /// last it holds, never from one it read past.</summary>
    [Fact]
    public async Task A_page_reads_past_posts_its_reader_may_not_view_and_continues_from_the_last_it_keeps()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        var oldest = await Started(Leo, "Older", "Body.");
        for (var index = 0; index < RecentPosts.PageSize + 5; index++) await Started(Leo, "Studio " + index, "Body.", Studio);
        var newer = new List<string>();
        for (var index = 0; index < RecentPosts.PageSize; index++) newer.Add((await Started(Leo, "Newer " + index, "Body.")).Id);
        newer.Reverse();

        var first = await RecentPosts.Read(Leo, new Tangents.ViewableTangents.Met(Ada), null, Cancellation);
        Assert.Equal(newer, first.Posts.Select(post => post.Id));
        Assert.Equal(newer[^1], first.Next?.Id);
        var second = await RecentPosts.Read(Leo, new Tangents.ViewableTangents.Met(Ada), first.Next, Cancellation);
        Assert.Equal([oldest.Id], second.Posts.Select(post => post.Id));
        Assert.Null(second.Next);
    }

    /// <summary>Each read past the first leaves out the Tangents met that the reader may not view, so where every read meets
    /// a Tangent no earlier one met, a page stops at its bound of reads, holds what it kept and continues from the last Post
    /// it read: the reading stays bounded and loses nothing.</summary>
    [Fact]
    public async Task A_page_reads_at_most_its_bound_and_an_older_page_continues_from_the_last_post_it_read()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var ada = await SignedIn(tangentSpace, Ada);
        var oldest = await Started(Leo, "Older", "Body.");
        for (var tangent = 0; tangent < Tangents.ViewableTangents.Met.ReadLimit; tangent++)
        {
            var slug = "back-room-" + tangent;
            await Given.ATangent(tangentSpace, slug, Audience.Private, Cancellation);
            for (var index = 0; index <= RecentPosts.PageSize; index++) await Started(Leo, slug + " " + index, "Body.", slug);
        }

        var first = await RecentPosts.Read(Leo, new Tangents.ViewableTangents.Met(Ada), null, Cancellation);
        Assert.Empty(first.Posts);
        Assert.NotNull(first.Next);
        var second = await RecentPosts.Read(Leo, new Tangents.ViewableTangents.Met(Ada), first.Next, Cancellation);
        Assert.Equal([oldest.Id], second.Posts.Select(post => post.Id));
        Assert.Null(second.Next);

        // The page says what lies further back, and its older page holds what Ada may read.
        var page = await Send(ada.Client, HttpMethod.Get, "/p/" + LeoHandle);
        Assert.Contains("Nothing you can read here; older posts lie further back.", page.Body);
        var json = (await Send(ada.Client, HttpMethod.Get, "/p/" + LeoHandle, json: true)).Json;
        var older = (await Send(ada.Client, HttpMethod.Get, new Uri(json.GetProperty("older").GetString()!).PathAndQuery, json: true)).Json;
        Assert.Equal([oldest.Id], Ids(older));
    }

    /// <summary>Whether the answer leaves the member out, as a null one is.</summary>
    private static bool Missing(JsonElement value, string name) => !value.TryGetProperty(name, out var member) || member.ValueKind == JsonValueKind.Null;

    private static string AddressOf(Post root, Post post) => BaseUrl + PostPath(root, post);

    private static List<string> Ids(JsonElement profile) => profile.GetProperty("posts").EnumerateArray().Select(post => post.GetProperty("id").GetString()!).ToList();

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToList();
}
