using System.Net;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Events;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Typing <c>@</c> or <c>#</c> offers who and where: the Tangent's suggestions name the people of the Topic first,
/// then the roles with how many hold each, and the Tangents the writer may view; a role mentioned reaches each holder's
/// door once, however many hold it and whether or not they are named too; and a Tangent's slug typed after <c>#</c> reads
/// as a link to it.</summary>
public sealed class MentionPickerTests
{
    private static async Task<List<(string Kind, string Insert, string? Note)>> Suggested(HttpClient client, string tangent, string typed, string? topic = null)
    {
        var answer = await Send(client, HttpMethod.Get,
            $"/t/{tangent}/{SuggestionsController.Segment}?q={Uri.EscapeDataString(typed)}" + (topic is null ? "" : "&topic=" + topic), json: true);
        Assert.Equal(HttpStatusCode.OK, answer.Status);
        return answer.Json.GetProperty("suggestions").EnumerateArray()
            .Select(entry => (entry.GetProperty("kind").GetString()!, entry.GetProperty("insert").GetString()!,
                entry.TryGetProperty("note", out var note) && note.ValueKind == JsonValueKind.String ? note.GetString() : null))
            .ToList();
    }

    [Fact]
    public async Task At_offers_the_topics_people_first_then_handles_by_their_start_and_the_roles_with_how_many_hold_each()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        using var mo = await SignedIn(tangentSpace, Mo);
        var root = await Started(Ada, "Lanterns", "Who lights theirs first?");
        await Replied(root, Bea, "I do.");

        var everyone = await Suggested(ada.Client, Workshop, "@", root.Slug);
        Assert.Equal(("person", "bea4writes.example"), (everyone[0].Kind, everyone[0].Insert));
        Assert.DoesNotContain(everyone, entry => entry.Insert == "ada4writes.example");
        Assert.Contains(("role", "moderator", "1 person"), everyone);

        var byStart = await Suggested(ada.Client, Workshop, "@mo4");
        Assert.Equal(("person", "mo4writes.example"), (byStart[0].Kind, byStart[0].Insert));

        Assert.Equal([("tangent", Workshop, null)], await Suggested(ada.Client, Workshop, "#work"));
        Assert.NotEqual(HttpStatusCode.OK, (await Send(Agent(tangentSpace), HttpMethod.Get, $"/t/{Studio}/{SuggestionsController.Segment}?q=%40", json: true)).Status);
    }

    [Fact]
    public async Task A_role_mentioned_reaches_each_holders_door_once_as_one_event_however_many_hold_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        using var mo = await SignedIn(tangentSpace, Mo);

        var root = await Started(Ada, "Lanterns", "Who lights theirs first?");
        var page = TopicPath(root);
        var replyId = Tids.Mint();
        var replied = await Json(ada, page, page, new { id = replyId, body = "@moderators could you pin this? cc @mo4writes.example" });
        Assert.Equal(HttpStatusCode.Created, replied.Status);

        // The role's mention is a link to its key, like a person's to their DID: the page marks it and the JSON names it.
        var reply = (await Post.Get(replyId, Cancellation))!;
        Assert.StartsWith("[@moderators](role:moderator) could you pin this?", reply.Body);
        Assert.Equal([Access.Roles.Moderator], reply.MentionedRoles);
        Assert.Contains("<strong>@moderators</strong> could you pin this?", (await Send(bea.Client, HttpMethod.Get, page)).Body);
        var shown = (await Send(bea.Client, HttpMethod.Get, page, json: true)).Json.GetProperty("replies")[0].GetProperty("mentions");
        Assert.Contains(shown.EnumerateArray(), mention => mention.GetProperty("id").GetString() == Access.Roles.Moderator
            && mention.GetProperty("handle").GetString() == "moderator");

        var roleRows = await Event.Query(e => e.Kind == EventKind.Mention && e.Role == Access.Roles.Moderator, Cancellation);
        Assert.Single(roleRows);
        Assert.Null(roleRows[0].TargetId);

        var forMo = (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(1, forMo.GetProperty("news").GetProperty("mentions").GetProperty("count").GetInt32());
        Assert.Equal(1, forMo.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32());
        var forBea = (await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(0, forBea.GetProperty("news").GetProperty("mentions").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task A_picked_mention_reaches_the_one_picked_even_when_their_handle_changed_before_it_was_sent()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        Assert.IsType<Outcome<Participants.Participant>.Done>(await new Participants.CaptureProfile(
            new Participants.ProfileAnswer(Bea, "Bea [the] *lamp*_", null, null), DateTimeOffset.UtcNow).Send(Cancellation));
        var root = await Started(Ada, "Lanterns", "Who lights theirs first?");
        await Replied(root, Bea, "I do.");

        // The pick names its target and its name: what the browser writes for it.
        var answer = await Send(ada.Client, HttpMethod.Get, $"/t/{Workshop}/{SuggestionsController.Segment}?q=%40bea&topic={root.Slug}", json: true);
        var picked = answer.Json.GetProperty("suggestions")[0];
        Assert.Equal(("person", "at://" + Bea, "Bea [the] *lamp*_"),
            (picked.GetProperty("kind").GetString(), picked.GetProperty("target").GetString(), picked.GetProperty("name").GetString()));
        var markdown = @"[@Bea \[the\] \*lamp\*\_](" + picked.GetProperty("target").GetString() + ")";

        // Bea's handle changes while Ada writes; the pick still means Bea.
        Assert.IsType<Outcome<Participants.Participant>.Done>(await new Participants.RecordHandle(Bea, "bea.renamed.example").Send(Cancellation));
        var page = TopicPath(root);
        var id = Tids.Mint();
        Assert.Equal(HttpStatusCode.Created, (await Json(ada, page, page, new { id, body = "Thanks " + markdown + "!" })).Status);

        var reply = (await Post.Get(id, Cancellation))!;
        Assert.Equal("Thanks " + markdown + "!", reply.Body);
        Assert.Equal([Bea], reply.MentionedIds);
        var told = Assert.Single(await Event.Query(e => e.Kind == EventKind.Mention && e.PostId == id, Cancellation));
        Assert.Equal(Bea, told.TargetId);
        Assert.Contains($"title=\"@bea.renamed.example\">@Bea [the] *lamp*_</a>!", (await Send(bea.Client, HttpMethod.Get, page)).Body);
    }

    [Fact]
    public async Task A_tangents_slug_typed_after_a_hash_reads_as_a_link_to_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var root = await Started(Ada, "Lanterns", "Who lights theirs first?");
        var page = TopicPath(root);
        var id = Tids.Mint();
        Assert.Equal(HttpStatusCode.Created, (await Json(ada, page, page, new { id, body = "More in #workshop, not #nowhere or `#workshop`." })).Status);

        Assert.Equal("More in [#workshop](/t/workshop), not #nowhere or `#workshop`.", (await Post.Get(id, Cancellation))!.Body);
        var html = (await Send(ada.Client, HttpMethod.Get, page)).Body;
        Assert.Contains("href=\"/t/workshop\">#workshop</a>", html);
    }
}
