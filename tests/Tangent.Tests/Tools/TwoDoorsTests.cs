using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Tools;

/// <summary>A form, a JSON call and a tool call reach the same action with the same input, under the same rules, and get the same
/// answer in their own shape, through a real host's <c>/mcp</c>.</summary>
public sealed class TwoDoorsTests
{
    private const string Leo = "did:plc:leo4writes";

    /// <summary>A place, Ada signed in, and a Topic of Bea's.</summary>
    private sealed class Doors : IDisposable
    {
        public required TangentSpaceFixture TangentSpace { get; init; }
        public required Browser AdaBrowser { get; init; }
        public required ToolClient AdaTools { get; init; }
        public required Post Root { get; init; }

        /// <summary>JSON posted with the session of <paramref name="by"/>, Ada's where none is named.</summary>
        public async Task<Answered> Posted(string path, object fields, Browser? by = null)
            => await Send((by ?? AdaBrowser).Client, HttpMethod.Post, path, JsonBody(fields), json: true);

        public void Dispose()
        {
            AdaTools.Dispose();
            AdaBrowser.Dispose();
        }
    }

    /// <summary>Not an async method, so the binding a fixture makes stays on the proof's flow.</summary>
    private static Task<TangentSpaceFixture> Starting() => TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);

    private static async Task<Doors> Open(TangentSpaceFixture tangentSpace)
    {
        await Place(tangentSpace);
        var root = await Started(Bea, "Lanterns", "How do you hang yours?");
        var browser = await SignedIn(tangentSpace, Ada);
        return new Doors
        {
            TangentSpace = tangentSpace,
            AdaBrowser = browser,
            AdaTools = await ToolClient.Connect(tangentSpace, browser),
            Root = root
        };
    }

    /// <summary>The route's answer on both doors, word for word, where it is a refusal.</summary>
    private static void SameRefusal(Answered http, Called tool)
    {
        Assert.True(tool.Failed);
        Assert.Equal((int)http.Status, tool.Status);
        Assert.Equal(JsonSerializer.Serialize(http.Json), JsonSerializer.Serialize(tool.Structured));
    }

    [Fact]
    public async Task A_reply_on_either_door_is_one_shape_from_the_same_author_holding_the_same_and_says_where_it_went()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        var root = doors.Root;

        var http = await doors.Posted(TopicPath(root), new { body = "By HTTP." });
        var tool = await doors.AdaTools.Call("reply", new { tangent = root.TangentId, topic = root.Slug, body = "By the tool." });

        Assert.Equal(HttpStatusCode.Created, http.Status);
        Assert.Equal((201, false), (tool.Status, tool.Failed));
        var (byHttp, byTool) = (http.Json, tool.Structured!.Value);
        Assert.Equal(
            string.Join(',', byHttp.EnumerateObject().Select(member => member.Name)),
            string.Join(',', byTool.EnumerateObject().Select(member => member.Name)));
        foreach (var same in new[] { "tangent", "topicId", "parentId", "authorId", "version", "removed" })
            Assert.Equal(byHttp.GetProperty(same).ToString(), byTool.GetProperty(same).ToString());
        Assert.Equal(byHttp.GetProperty("can").ToString(), byTool.GetProperty("can").ToString());
        Assert.Equal((Ada, "By the tool."), (byTool.GetProperty("authorId").GetString(), byTool.GetProperty("body").GetString()));
        Assert.Equal(byHttp.GetProperty("sequence").GetInt64() + 1, byTool.GetProperty("sequence").GetInt64());
        Assert.Equal(byHttp.GetProperty("address").GetString(), http.Location);
        Assert.Equal(byTool.GetProperty("address").GetString(), tool.Location);
    }

    [Fact]
    public async Task An_absent_topic_and_a_hidden_one_answer_alike_on_both_doors()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        var hidden = Post.Root(Tids.Mint(), Studio, "Plans", "plans", PostKind.Topic, "Not for everyone.", Mo, DateTimeOffset.UtcNow);
        Assert.IsType<Outcome<Post>.Done>(await new Store<Post>(hidden).Send(Cancellation));

        var nothing = await doors.Posted("/t/" + Workshop + "/nothing", new { body = "Anyone?" });
        var missing = await doors.AdaTools.Call("reply", new { tangent = Workshop, topic = "nothing", body = "Anyone?" });
        var concealed = await doors.AdaTools.Call("reply", new { tangent = Studio, topic = "plans", body = "Anyone?" });
        var read = await doors.AdaTools.Call("read_topic", new { tangent = Studio, topic = "plans" });

        Assert.Equal((HttpStatusCode.NotFound, "absent"), (nothing.Status, nothing.Kind));
        SameRefusal(nothing, missing);
        SameRefusal(nothing, concealed);
        SameRefusal(nothing, read);
        Assert.Equal("Nothing answers at this address.", missing.Text);
    }

    [Fact]
    public async Task A_private_places_visitor_is_asked_to_sign_in_on_both_doors_and_a_participant_reads_on_both()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", Audience.Private, ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var http = Agent(tangentSpace);

        using var visitor = await ToolClient.Connect(tangentSpace);
        var page = await Send(http, HttpMethod.Get, TopicPath(root), json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word), (page.Status, page.Word));
        SameRefusal(page, await visitor.Call("read_topic", new { tangent = Workshop, topic = root.Slug }));

        using var ada = await SignedIn(tangentSpace, Ada);
        using var tools = await ToolClient.Connect(tangentSpace, ada);
        var read = await tools.Call("read_topic", new { tangent = Workshop, topic = root.Slug });
        var onHttp = await Send(ada.Client, HttpMethod.Get, TopicPath(root), json: true);
        Assert.Equal((200, HttpStatusCode.OK), (read.Status, onHttp.Status));
        Assert.Equal(root.Id, read.Structured!.Value.GetProperty("id").GetString());
        Assert.Equal(onHttp.Json.GetProperty("root").GetProperty("body").GetString(), read.Structured!.Value.GetProperty("root").GetProperty("body").GetString());
    }

    [Fact]
    public async Task A_gate_says_its_word_and_its_sentence_on_both_doors()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        var root = doors.Root;

        // A word and a sentence.
        var http = await doors.Posted("/t/new", new { name = "Mine" });
        var tool = await doors.AdaTools.Call("create_tangent", new { name = "Mine" });
        Assert.Equal(HttpStatusCode.Forbidden, http.Status);
        Assert.False(string.IsNullOrEmpty(http.Word));
        SameRefusal(http, tool);
        Assert.Equal("gated", tool.ErrorCode);

        // A session whose participant is no longer here reads as a visitor: sign in. Its session still holds, so it is
        // forbidden rather than unauthorized.
        var row = await Participant.Get(Ada, Cancellation);
        await row!.Remove(Cancellation);
        var signIn = await doors.Posted(TopicPath(root), new { body = "Anyone?" });
        var asked = await doors.AdaTools.Call("reply", new { tangent = root.TangentId, topic = root.Slug, body = "Anyone?" });
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.SignIn.Word), (signIn.Status, signIn.Word));
        SameRefusal(signIn, asked);
    }

    [Fact]
    public async Task A_forged_author_binds_to_nothing_on_any_door()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        var root = doors.Root;
        var forged = new { tangent = root.TangentId, topic = root.Slug, body = "Mine.", author = Mo, authorId = Mo };

        var http = await doors.Posted(TopicPath(root), new { body = "Mine by HTTP.", author = Mo, authorId = Mo });
        var tool = await doors.AdaTools.Call("reply", forged);
        var page = TopicPath(root);
        var form = await Form(doors.AdaBrowser, page, page, new Dictionary<string, string> { ["body"] = "Mine by form.", ["author"] = Mo, ["authorId"] = Mo });

        Assert.Equal(Ada, http.Json.GetProperty("authorId").GetString());
        Assert.Equal(Ada, tool.Structured!.Value.GetProperty("authorId").GetString());
        Assert.Equal(HttpStatusCode.SeeOther, form.Status);
        Assert.Empty(await Post.Query(post => post.AuthorId == Mo, Cancellation));
        Assert.Equal(3, (await Post.Query(post => post.AuthorId == Ada, Cancellation)).Count);
    }

    [Fact]
    public async Task A_field_of_a_form_or_a_call_never_names_the_address_and_a_tangent_field_names_where_a_topic_starts_on_every_door()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        var root = doors.Root;
        var page = TopicPath(root);
        var taking = PostPath(root, root) + "/tangent";
        await Given.ATangent(doors.TangentSpace, "elsewhere", ct: Cancellation);

        var form = await Form(doors.AdaBrowser, page, page, new Dictionary<string, string> { ["body"] = "Stays here.", ["tangent"] = "elsewhere", ["topic"] = "nothing" });
        Assert.Equal(HttpStatusCode.SeeOther, form.Status);
        Assert.Equal(root.Id, Assert.Single(await Post.Query(post => post.Body == "Stays here.", Cancellation)).TopicId);

        var byForm = await Form(doors.AdaBrowser, page, taking, new Dictionary<string, string> { ["tangent"] = "elsewhere", ["title"] = "By form" });
        var byJson = await doors.Posted(taking, new { tangent = "elsewhere", title = "By JSON" });
        var byTool = await doors.AdaTools.Call("take_on_a_tangent", new { from = root.TangentId, topic = root.Slug, post = root.Id, tangent = "elsewhere", title = "By the tool" });

        Assert.Equal(HttpStatusCode.SeeOther, byForm.Status);
        Assert.Equal(HttpStatusCode.Created, byJson.Status);
        Assert.Equal(201, byTool.Status);
        Assert.All(await Post.Query(post => post.OriginId == root.Id, Cancellation), taken => Assert.Equal("elsewhere", taken.TangentId));
        Assert.Equal(3, (await Post.Query(post => post.OriginId == root.Id, Cancellation)).Count);
    }

    [Fact]
    public async Task A_value_that_cannot_bind_is_refused_naming_its_field_on_both_doors_and_a_malformed_body_is_refused_alike_at_any_address()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        var root = await Started(Ada, "Hooks", "Brass or iron?");
        var before = await Counted();

        var http = await doors.Posted(PostPath(root, root) + "/edit", new { body = "New words.", version = "not a number" });
        var tool = await doors.AdaTools.Call("edit", new { tangent = root.TangentId, topic = root.Slug, post = root.Id, body = "New words.", version = "not a number" });
        Assert.Equal(HttpStatusCode.BadRequest, http.Status);
        Assert.Equal("application/problem+json", http.MediaType);
        Assert.True(http.Json.GetProperty("errors").TryGetProperty("version", out _), http.Body);
        SameRefusal(http, tool);
        Assert.Equal("invalid", tool.ErrorCode);
        Assert.Contains("version", tool.Text);

        // Left out, the rule says what it wants.
        var unnamed = await doors.Posted(PostPath(root, root) + "/edit", new { body = "New words." });
        var named = await doors.AdaTools.Call("edit", new { tangent = root.TangentId, topic = root.Slug, post = root.Id, body = "New words." });
        Assert.Equal(PostWrites.VersionRule.Text, unnamed.Json.GetProperty("detail").GetString());
        SameRefusal(unnamed, named);

        // A body of neither shape says so where the sender may write, and an address that holds nothing says only that.
        using var array = new StringContent("[]", System.Text.Encoding.UTF8, "application/json");
        var malformed = await Send(doors.AdaBrowser.Client, HttpMethod.Post, TopicPath(root), array, json: true);
        using var elsewhere = new StringContent("[]", System.Text.Encoding.UTF8, "application/json");
        var nowhere = await Send(doors.AdaBrowser.Client, HttpMethod.Post, "/t/nowhere/nothing", elsewhere, json: true);
        Assert.Equal((HttpStatusCode.BadRequest, "invalid"), (malformed.Status, malformed.Kind));
        Assert.Equal((HttpStatusCode.NotFound, "absent"), (nowhere.Status, nowhere.Kind));
        Assert.Equal(before, await Counted());
    }

    [Fact]
    public async Task A_roles_permissions_come_one_to_an_item_or_in_one_value_and_left_out_stay_and_an_empty_list_grants_none()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var tools = await ToolClient.Connect(tangentSpace, leo);

        async Task<IReadOnlyList<string?>> Granted(Called answer)
        {
            Assert.False(answer.Failed, answer.Text);
            return answer.Structured!.Value.GetProperty("permissions").EnumerateArray().Select(bit => bit.GetString()).ToList();
        }

        Assert.Equal(["View", "Post"], await Granted(await tools.Call("create_role", new { name = "Keepers", permissions = new[] { "View", "Post" } })));
        Assert.Equal(["View", "Topic"], await Granted(await tools.Call("change_role", new { role = "keepers", permissions = new[] { "View Topic" } })));

        // Left out they stay; an empty list grants none, on either door.
        Assert.Equal(["View", "Topic"], await Granted(await tools.Call("change_role", new { role = "keepers", name = "The keepers" })));
        Assert.Empty(await Granted(await tools.Call("change_role", new { role = "keepers", permissions = Array.Empty<string>() })));
        Assert.Equal(HttpStatusCode.OK, (await doors.Posted(RolesController.ChangePath, new { role = "keepers", permissions = "View" }, leo)).Status);
        var emptied = await doors.Posted(RolesController.ChangePath, new { role = "keepers", permissions = Array.Empty<string>() }, leo);
        Assert.Equal(0, emptied.Json.GetProperty("permissions").GetArrayLength());
    }

    [Fact]
    public async Task A_body_past_the_limit_writes_nothing_and_is_refused_on_both_doors()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        var root = doors.Root;
        var before = await Counted();
        var words = new string('x', (int)PostWrites.RequestLimit + 1);

        var http = await doors.Posted(TopicPath(root), new { body = words });
        var tool = await doors.AdaTools.Call("reply", new { tangent = root.TangentId, topic = root.Slug, body = words });

        Assert.Equal((HttpStatusCode.RequestEntityTooLarge, "invalid"), (http.Status, http.Kind));
        Assert.Equal(413, tool.Status);
        Assert.Equal("invalid", tool.ErrorCode);
        Assert.Equal(before, await Counted());
    }

    [Fact]
    public async Task A_field_sent_empty_is_not_a_field_left_out_on_any_door()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        await Given.AParticipant(doors.TangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(doors.TangentSpace, Leo);
        using var tools = await ToolClient.Connect(doors.TangentSpace, leo);
        const string settings = "/t/" + Workshop + "/settings";

        async Task<string> Description() => (await Tangents.Tangent.Get(Workshop, Cancellation))!.Description;

        Assert.Equal(200, (await tools.Call("change_tangent", new { tangent = Workshop, description = "For the bench." })).Status);
        Assert.Equal("For the bench.", await Description());
        Assert.Equal(200, (await tools.Call("change_tangent", new { tangent = Workshop, name = "The workshop" })).Status);
        Assert.Equal("For the bench.", await Description());
        Assert.Equal(200, (await tools.Call("change_tangent", new { tangent = Workshop, description = "" })).Status);
        Assert.Equal("", await Description());

        await tools.Call("change_tangent", new { tangent = Workshop, description = "For the bench." });
        var form = await Form(leo, settings, settings, new Dictionary<string, string> { ["description"] = "" });
        Assert.Equal(HttpStatusCode.SeeOther, form.Status);
        Assert.Equal("", await Description());
        await tools.Call("change_tangent", new { tangent = Workshop, description = "For the bench." });
        var json = await Send(leo.Client, HttpMethod.Post, settings, JsonBody(new { description = "" }), json: true);
        Assert.Equal(HttpStatusCode.OK, json.Status);
        Assert.Equal("", await Description());
    }

    [Fact]
    public async Task A_retry_with_its_id_makes_one_post_on_either_door_and_across_them_and_without_one_each_send_is_new()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        var root = doors.Root;
        var id = Tids.Mint();
        object Sent(string? sent) => new { tangent = root.TangentId, topic = root.Slug, body = "Whoever is first home.", id = sent };

        var first = await doors.AdaTools.Call("reply", Sent(id));
        var again = await doors.AdaTools.Call("reply", Sent(id));
        var overHttp = await doors.Posted(TopicPath(root), new { id, body = "Whoever is first home." });
        Assert.Equal((201, id), (first.Status, first.Structured!.Value.GetProperty("id").GetString()));
        Assert.Equal(id, again.Structured!.Value.GetProperty("id").GetString());
        Assert.Equal(id, overHttp.Json.GetProperty("id").GetString());
        Assert.Equal(1, await CountOf("Whoever is first home."));

        // With no id, the same words sent again are another Post, standing after everything before it.
        var unnamed = await doors.AdaTools.Call("reply", Sent(null));
        var unnamedAgain = await doors.Posted(TopicPath(root), new { body = "Whoever is first home." });
        Assert.True(Tid.IsValid(unnamed.Structured!.Value.GetProperty("id").GetString()));
        Assert.NotEqual(unnamed.Structured!.Value.GetProperty("id").GetString(), unnamedAgain.Json.GetProperty("id").GetString());
        Assert.True(unnamedAgain.Json.GetProperty("sequence").GetInt64() > first.Structured!.Value.GetProperty("sequence").GetInt64());
        Assert.Equal(3, await CountOf("Whoever is first home."));

        static async Task<long> CountOf(string body) => (await Post.Query(post => post.Body == body, Cancellation)).Count;
    }

    [Fact]
    public async Task An_id_reused_with_other_words_is_a_conflict_on_both_doors_and_an_id_of_another_shape_is_refused_alike()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        var root = doors.Root;
        var id = Tids.Mint();

        Assert.Equal(HttpStatusCode.Created, (await doors.Posted(TopicPath(root), new { id, body = "These words." })).Status);
        var http = await doors.Posted(TopicPath(root), new { id, body = "Other words." });
        var tool = await doors.AdaTools.Call("reply", new { tangent = root.TangentId, topic = root.Slug, id, body = "Other words." });
        Assert.Equal(HttpStatusCode.Conflict, http.Status);
        SameRefusal(http, tool);
        Assert.Equal("conflict", tool.ErrorCode);

        var unshaped = await doors.Posted(TopicPath(root), new { id = "not-a-tid", body = "These words." });
        var unshapedTool = await doors.AdaTools.Call("reply", new { tangent = root.TangentId, topic = root.Slug, id = "not-a-tid", body = "These words." });
        Assert.Equal((HttpStatusCode.BadRequest, Post.NeedsTid.Text), (unshaped.Status, unshaped.Json.GetProperty("detail").GetString()));
        SameRefusal(unshaped, unshapedTool);
    }

    [Fact]
    public async Task What_a_role_may_do_is_judged_by_the_callers_role_on_both_doors()
    {
        await using var tangentSpace = await Starting();
        using var doors = await Open(tangentSpace);
        var root = doors.Root;
        var second = await Replied(root, Bea, "A second reply.");
        using var mo = await SignedIn(doors.TangentSpace, Mo);
        using var moTools = await ToolClient.Connect(doors.TangentSpace, mo);

        object Removing(Post post) => new { tangent = post.TangentId, topic = root.Slug, post = post.Id };
        var memberHttp = await doors.Posted(PostPath(root, root) + "/remove", new { });
        var memberTool = await doors.AdaTools.Call("remove", Removing(root));
        Assert.Equal(HttpStatusCode.Forbidden, memberHttp.Status);
        SameRefusal(memberHttp, memberTool);
        Assert.False((await Post.Get(root.Id, Cancellation))!.Removed);

        var moderatorTool = await moTools.Call("remove", Removing(second));
        var moderatorHttp = await doors.Posted(PostPath(root, root) + "/remove", new { }, mo);
        Assert.Equal((200, HttpStatusCode.OK), (moderatorTool.Status, moderatorHttp.Status));
        Assert.True((await Post.Get(second.Id, Cancellation))!.Removed);
        Assert.True((await Post.Get(root.Id, Cancellation))!.Removed);

        // People, the same.
        var timedOut = await moTools.Call("time_out", new { participant = Bea, @for = "hour" });
        Assert.Equal((200, "timed-out"), (timedOut.Status, timedOut.Structured!.Value.GetProperty("standing").GetString()));
        var refused = await doors.AdaTools.Call("time_out", new { participant = Bea, @for = "hour" });
        var refusedHttp = await doors.Posted(PeopleController.TimeOutPath, new { participant = Bea, @for = "hour" });
        Assert.Equal(HttpStatusCode.Forbidden, refusedHttp.Status);
        SameRefusal(refusedHttp, refused);
    }
}
