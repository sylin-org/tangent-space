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

    /// <summary>A page reads only the Tangents the reader may view: however many Posts lie in the others, a page is
    /// full of those the reader may view, and neither its length nor its cursor says anything of the rest.</summary>
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
        for (var index = 0; index < 101; index++) hidden.Add((await Started(Leo, "Studio " + index, "Body.", Studio)).Id);
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

    /// <summary>Your own approvals page: past one page of them, the rest are a link away, and nothing is left out.</summary>
    [Fact]
    public async Task A_page_keeps_only_viewable_posts_and_continues_from_the_last_it_keeps()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Started(Leo, "Older", "Body.");
        for (var index = 0; index < RecentPosts.PageSize + 5; index++) await Started(Leo, "Studio " + index, "Body.", Studio);
        var newest = await Started(Leo, "Newest", "Body.");
        var viewable = new HashSet<string>([Workshop], StringComparer.Ordinal);

        var first = await RecentPosts.Read(Leo, viewable, [], null, Cancellation);
        Assert.Equal([newest.Id], first.Posts.Select(post => post.Id));
        Assert.Equal(newest.Id, first.Next?.Id);
        var second = await RecentPosts.Read(Leo, viewable, [], first.Next, Cancellation);
        Assert.Empty(second.Posts);
        Assert.Null(second.Next);
    }

    /// <summary>Whether the answer leaves the member out, as a null one is.</summary>
    private static bool Missing(JsonElement value, string name) => !value.TryGetProperty(name, out var member) || member.ValueKind == JsonValueKind.Null;

    private static string AddressOf(Post root, Post post) => BaseUrl + PostPath(root, post);

    private static List<string> Ids(JsonElement profile) => profile.GetProperty("posts").EnumerateArray().Select(post => post.GetProperty("id").GetString()!).ToList();

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToList();
}
