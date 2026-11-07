using System.Net;
using System.Text;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Every write route and form page resolves its address as the Topic page does before it judges anything of
/// the request: the place's gate for every address alike, then the missing resource's answer for whatever is missing
/// or hidden, so an address that holds something the reader may not see answers exactly as one that holds nothing.</summary>
public sealed class AddressTests
{
    [Fact]
    public async Task A_request_of_any_shape_to_a_hidden_target_answers_exactly_as_the_same_request_to_a_missing_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var hidden = Given.ARoot(Tids.Mint(), Studio, "Plans", "plans", PostKind.Topic, "Not for everyone.", Mo, DateTimeOffset.UtcNow);
        Assert.IsType<Outcome<Post>.Done>(await new Store<Post>(hidden).Send(Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);
        var before = await Counted();

        async Task<(HttpStatusCode, string?)> Asked(string path, HttpContent content, HttpMethod? method = null)
        {
            var answered = await Send(ada.Client, method ?? HttpMethod.Post, path, content, json: true);
            return (answered.Status, answered.Kind);
        }

        static StringContent Array() => new("[]", Encoding.UTF8, "application/json");
        var missing = (HttpStatusCode.NotFound, "absent");

        Assert.Equal(missing, await Asked("/t/studio/plans/" + hidden.Id, JsonBody(new { body = "x" }), HttpMethod.Patch));
        Assert.Equal(missing, await Asked("/t/studio/plans/" + Tids.Mint(), JsonBody(new { body = "x" }), HttpMethod.Patch));
        // The address is judged before anything is read of the body, so a body that is not one JSON object, sent to an address
        // that holds something hidden or nothing, answers what a missing address answers.
        Assert.Equal(missing, await Asked("/t/studio/plans/" + hidden.Id, Array(), HttpMethod.Patch));
        Assert.Equal(missing, await Asked("/t/studio/plans", Array()));
        Assert.Equal(missing, await Asked("/t/studio/nothing", Array()));
        Assert.Equal(missing, await Asked("/t/studio", Array()));
        Assert.Equal(missing, await Asked("/t/nowhere", Array()));
        // An address no Tangent has, and one whose slug no Tangent uses yet, answer as the hidden one does.
        var unused = await Send(ada.Client, HttpMethod.Get, "/t/unused-slug/new", json: true);
        var nowhere = await Send(ada.Client, HttpMethod.Get, "/t/nowhere/new", json: true);
        var studio = await Send(ada.Client, HttpMethod.Get, "/t/studio/new", json: true);
        Assert.Equal(missing, (unused.Status, unused.Kind));
        Assert.Equal((unused.Status, unused.Body), (nowhere.Status, nowhere.Body));
        Assert.Equal((unused.Status, unused.Body), (studio.Status, studio.Body));
        Assert.Equal(before, await Counted());
    }

    [Fact]
    public async Task On_a_private_place_a_visitor_reads_the_same_sign_in_for_every_address_whether_it_holds_something_or_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", Audience.Private, ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var visitor = Agent(tangentSpace);
        var before = await Counted();

        var addresses = new[]
        {
            (HttpMethod.Get, PostPath(root, root) + "/edit", (HttpContent?)null),
            (HttpMethod.Get, TopicPath(root) + "/" + Tids.Mint() + "/edit", null),
            (HttpMethod.Get, "/t/workshop/nothing/" + Tids.Mint() + "/edit", null),
            (HttpMethod.Get, "/t/nowhere/nothing/" + Tids.Mint() + "/edit", null),
            (HttpMethod.Get, "/t/workshop/new", null),
            (HttpMethod.Get, "/t/nowhere/new", null),
            (HttpMethod.Patch, PostPath(root, root), JsonBody(new { body = "x", version = 1 })),
            (HttpMethod.Post, TopicPath(root) + "/" + Tids.Mint() + "/remove", JsonBody(new { })),
            (HttpMethod.Post, PostPath(root, root) + "/remove", JsonBody(new { })),
            (HttpMethod.Post, TopicPath(root), JsonBody(new { id = Tids.Mint(), body = "x" })),
            (HttpMethod.Post, "/t/workshop/nothing", JsonBody(new { id = Tids.Mint(), body = "x" })),
            (HttpMethod.Post, "/t/workshop", JsonBody(new { id = Tids.Mint(), title = "x", body = "x" })),
            (HttpMethod.Post, "/t/nowhere", JsonBody(new { id = Tids.Mint(), title = "x", body = "x" }))
        };
        var answers = new List<(HttpStatusCode, string?, string?, string)>();
        foreach (var (method, path, content) in addresses)
        {
            var answered = await Send(visitor, method, path, content, json: true);
            answers.Add((answered.Status, answered.Title, answered.Word, System.Text.RegularExpressions.Regex.Replace(answered.Body, "\"next\":\"[^\"]*\"", "")));
        }

        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word), (answers[0].Item1, answers[0].Item3));
        Assert.All(answers, answer => Assert.Equal(answers[0], answer));
        Assert.Equal(before, await Counted());
    }

    [Fact]
    public async Task A_reply_whose_body_passes_the_cap_is_refused_and_writes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var ada = await SignedIn(tangentSpace, Ada);
        var before = await Counted();

        var answered = await Send(ada.Client, HttpMethod.Post, TopicPath(root), JsonBody(new { id = Tids.Mint(), body = new string('x', 3 * 1024 * 1024) }), json: true);

        // A body past the limit is refused where it is read, before the action.
        Assert.Equal((HttpStatusCode.RequestEntityTooLarge, "invalid"), (answered.Status, answered.Kind));
        Assert.Equal(before, await Counted());
    }
}
