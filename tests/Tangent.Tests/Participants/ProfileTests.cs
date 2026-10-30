using System.Net;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Koan.Web.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Delegates;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Participants;

/// <summary>A participant's profile at <c>/p/{handle}</c>: the profile their PDS gave, their roles, when they arrived
/// and their recent Posts in the Tangents the reader may view, both ways, with a cursor; absent where the place or the
/// handle is; names on Posts and the door linking to it; and, on the reader's own profile alone, the agents they
/// approved, each revoked in the page through Koan.</summary>
public sealed class ProfileTests
{
    private const string Leo = "did:plc:leo4profile";
    private const string Ann = "did:plc:ann4profile";
    private const string LeoHandle = "leo4profile.example";
    private const string AdaHandle = "ada4writes.example";

    [Fact]
    public async Task A_profile_shows_the_pds_profile_roles_arrival_and_recent_posts_and_leaves_out_a_tangent_the_reader_may_not_view()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ann, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var ann = await SignedIn(tangentSpace, Ann);
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new CaptureProfile(
            new ProfileAnswer(Leo, "Leo Botinelly", "Builds places to talk.", "https://cdn.example/leo.jpg"), DateTimeOffset.UtcNow), Cancellation));

        var open = await Started(writer, Leo, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(writer, open, Leo, "I do, at six.");
        var secret = await Started(writer, Leo, "The kiln budget", "Numbers for the studio.", Studio);
        var removed = await Started(writer, Leo, "A draft", "Gone soon.");
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(removed.Id, Leo), Cancellation));

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
        Assert.True(Missing(profile, "delegates"));

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
        var writer = await Place(tangentSpace);
        (await SignedIn(tangentSpace, Ada)).Dispose();
        await Started(writer, Ada, "Wicks", "Cotton or hemp?");
        using var visitor = Agent(tangentSpace);

        var page = await Send(visitor, HttpMethod.Get, "/p/" + AdaHandle);
        var json = await Send(visitor, HttpMethod.Get, "/p/" + AdaHandle, json: true);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Wicks", page.Body);
        Assert.Equal((Capabilities.SignIn, BaseUrl + "/sign-in?return=%2Fp%2F" + AdaHandle),
            (json.Json.GetProperty("gate").GetString(), json.Json.GetProperty("signIn").GetString()));
        Assert.DoesNotContain("Post", Strings(json.Json.GetProperty("can")));

        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Audience = Audience.Private;
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Store<TangentSpace>(space), Cancellation));

        var held = await Send(visitor, HttpMethod.Get, "/p/" + AdaHandle, json: true);
        var unheld = await Send(visitor, HttpMethod.Get, "/p/nobody.example", json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (held.Status, held.Gate));
        Assert.Equal(held.Body, unheld.Body);
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
        Assert.Equal((HttpStatusCode.NotFound, Capabilities.Absent), (unknown.Status, unknown.Gate));
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
        var byDid = await Send(visitor, HttpMethod.Get, "/p/" + Uri.EscapeDataString(Ada), json: true);
        Assert.Equal((HttpStatusCode.OK, Ada, BaseUrl + "/p/" + Uri.EscapeDataString(Ada)),
            (byDid.Status, byDid.Json.GetProperty("handle").GetString(), byDid.Json.GetProperty("address").GetString()));
    }

    [Fact]
    public async Task Names_on_posts_and_on_the_door_link_to_their_profiles()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        (await SignedIn(tangentSpace, Ada)).Dispose();
        var root = await Started(writer, Ada, "Wicks", "Cotton or hemp?");
        using var visitor = Agent(tangentSpace);

        var topic = await Send(visitor, HttpMethod.Get, TopicPath(root));
        var door = await Send(visitor, HttpMethod.Get, "/");
        var doorJson = await Send(visitor, HttpMethod.Get, "/", json: true);

        Assert.Contains($"<a class=\"post-author\" href=\"{BaseUrl}/p/{AdaHandle}\">", topic.Body);
        Assert.Contains($"<a class=\"around-name\" href=\"{BaseUrl}/p/{AdaHandle}\">", door.Body);
        Assert.Equal(BaseUrl + "/p/" + AdaHandle, doorJson.Json.GetProperty("around").GetProperty("people")[0].GetProperty("profile").GetString());
    }

    [Fact]
    public async Task Your_own_profile_lists_the_agents_you_approved_revoke_removes_one_through_koan_and_nobody_else_sees_them()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        // Koan keeps grants that expired; more of them than the profile reads, stored before the live one, leave it listed.
        for (var index = 0; index < Approved.Limit + 10; index++)
            await new AgentGrant
            {
                Subject = Ada, Capability = "mcp:grant:expired:" + index, Resource = BaseUrl, ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
                IssuedAt = DateTimeOffset.UtcNow.AddDays(-31), ClientId = DelegateClient.Id
            }.Save(Cancellation);
        var bearer = await Delegate(tangentSpace, ada);
        const string own = "/p/" + AdaHandle;

        var grant = Assert.Single(await AgentGrant.Query(row => row.Subject == Ada && row.ExpiresAt > DateTimeOffset.UtcNow, Cancellation));

        var mine = await Send(ada.Client, HttpMethod.Get, own, json: true);
        var agents = mine.Json.GetProperty("delegates").GetProperty("agents");
        Assert.Equal(1, agents.GetArrayLength());
        Assert.Equal(grant.Id, agents[0].GetProperty("id").GetString());
        Assert.Equal(grant.IssuedAt, agents[0].GetProperty("approved").GetDateTimeOffset());
        Assert.Equal((DelegateClient.Name, false), (agents[0].GetProperty("client").GetString(), agents[0].GetProperty("selfNamed").GetBoolean()));
        var revoke = new Uri(agents[0].GetProperty("revoke").GetString()!).AbsolutePath;
        Assert.Equal($"{own}/delegates/{grant.Id}/revoke", revoke);
        var page = await Send(ada.Client, HttpMethod.Get, own);
        Assert.Contains("Agents acting as you", page.Body);
        Assert.Contains($"<span class=\"profile-delegate-name\">{DelegateClient.Name}</span>", page.Body);
        Assert.Contains("<summary class=\"post-remove-toggle\">Revoke</summary>", page.Body);

        // Nobody else sees them: another participant, a visitor, nor Ada's own delegate reading as her.
        Assert.True(Missing((await Send(bea.Client, HttpMethod.Get, own, json: true)).Json, "delegates"));
        using var visitor = Agent(tangentSpace);
        Assert.True(Missing((await Send(visitor, HttpMethod.Get, own, json: true)).Json, "delegates"));
        var asDelegate = await Send(visitor, HttpMethod.Get, own, json: true, bearer: bearer);
        Assert.Equal((HttpStatusCode.OK, true), (asDelegate.Status, Missing(asDelegate.Json, "delegates")));
        Assert.DoesNotContain("Agents acting as you", (await Send(bea.Client, HttpMethod.Get, own)).Body);

        // Neither someone else nor the delegate revokes it.
        var byBea = await Form(bea, "/t/" + Workshop + "/new", revoke, new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.NotFound, byBea.Status);
        var byBearer = await Send(visitor, HttpMethod.Post, revoke, FormBody(new Dictionary<string, string>()), bearer: bearer);
        Assert.Equal(HttpStatusCode.Forbidden, byBearer.Status);
        Assert.NotNull(await AgentGrant.Get(grant.Id, Cancellation));

        var revoked = await Form(ada, own, revoke, new Dictionary<string, string>());
        Assert.Equal((HttpStatusCode.SeeOther, own), (revoked.Status, revoked.Location));
        Assert.Null(await AgentGrant.Get(grant.Id, Cancellation));
        Assert.Equal(0, (await Send(ada.Client, HttpMethod.Get, own, json: true)).Json.GetProperty("delegates").GetProperty("agents").GetArrayLength());
        Assert.Contains("No agent acts as you here.", (await Send(ada.Client, HttpMethod.Get, own)).Body);
    }

    [Fact]
    public async Task Each_approval_of_the_same_client_is_its_own_row_and_revokes_alone()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        await Delegate(tangentSpace, ada);
        await Delegate(tangentSpace, ada);
        const string own = "/p/" + AdaHandle;

        var agents = (await Send(ada.Client, HttpMethod.Get, own, json: true)).Json.GetProperty("delegates").GetProperty("agents");
        Assert.Equal(2, agents.GetArrayLength());
        Assert.True(agents[0].GetProperty("approved").GetDateTimeOffset() >= agents[1].GetProperty("approved").GetDateTimeOffset());
        var newest = agents[0].GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.SeeOther, (await Form(ada, own, new Uri(agents[0].GetProperty("revoke").GetString()!).AbsolutePath, new Dictionary<string, string>())).Status);

        var left = (await Send(ada.Client, HttpMethod.Get, own, json: true)).Json.GetProperty("delegates").GetProperty("agents");
        Assert.Equal(1, left.GetArrayLength());
        Assert.NotEqual(newest, left[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task The_json_carries_can_gate_and_a_cursor_through_the_posts()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var written = new List<string>();
        for (var index = 0; index < RecentPosts.PageSize + 5; index++)
            written.Add((await Started(writer, Ada, "Topic " + index, "Body " + index)).Id);
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

    /// <summary>A page reads one scan of the participant's Posts: where more of them lie in Tangents the reader may not
    /// view than one scan holds, the page holds fewer than its size and continues past them to the Posts the reader may
    /// view, none of the others shown.</summary>
    [Fact]
    public async Task Posts_past_more_than_a_scan_the_reader_may_not_view_are_reached_on_the_next_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var leo = await SignedIn(tangentSpace, Leo);
        var older = new List<string>();
        for (var index = 0; index < 2; index++) older.Add((await Started(writer, Leo, "Older " + index, "Body.")).Id);
        for (var index = 0; index < RecentPosts.ScanSize + 1; index++) await Started(writer, Leo, "Studio " + index, "Body.", Studio);
        var newer = new List<string>();
        for (var index = 0; index < 3; index++) newer.Add((await Started(writer, Leo, "Newer " + index, "Body.")).Id);
        older.Reverse();
        newer.Reverse();

        var first = (await Send(ada.Client, HttpMethod.Get, "/p/" + LeoHandle, json: true)).Json;
        Assert.Equal(newer, Ids(first));
        var next = new Uri(first.GetProperty("older").GetString()!);
        var second = (await Send(ada.Client, HttpMethod.Get, next.PathAndQuery, json: true)).Json;
        Assert.Equal(older, Ids(second));
        Assert.True(Missing(second, "older"));

        // An administrator may view the studio: their first page is full of it after the newest.
        var asLeo = (await Send(leo.Client, HttpMethod.Get, "/p/" + LeoHandle, json: true)).Json;
        Assert.Equal(RecentPosts.PageSize, Ids(asLeo).Count);
        Assert.Equal(newer, Ids(asLeo).Take(3));
    }

    /// <summary>Whether the answer leaves the member out, as a null one is.</summary>
    private static bool Missing(JsonElement value, string name) => !value.TryGetProperty(name, out var member) || member.ValueKind == JsonValueKind.Null;

    private static string AddressOf(Post root, Post post) => BaseUrl + PostPath(root, post);

    private static List<string> Ids(JsonElement profile) => profile.GetProperty("posts").EnumerateArray().Select(post => post.GetProperty("id").GetString()!).ToList();

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToList();
}
