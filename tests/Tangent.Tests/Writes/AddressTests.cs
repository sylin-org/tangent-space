using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Space;
using Tangent.Writer;
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
        var writer = await Place(tangentSpace);
        var hidden = Post.Root(Tids.Mint(), Studio, "Plans", "plans", PostKind.Topic, "Not for everyone.", Mo, DateTimeOffset.UtcNow);
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(hidden), Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);
        var token = (await ada.Token("/t/" + Workshop + "/new", Cancellation)).Values.Single();
        var before = await Counted();

        async Task<(HttpStatusCode, string?, string?)> Asked(string path, HttpContent content)
        {
            var answered = await Send(ada.Client, HttpMethod.Post, path, content, json: true, token: token);
            return (answered.Status, answered.Title, answered.Gate);
        }

        static StringContent Array() => new("[]", Encoding.UTF8, "application/json");
        var missing = (HttpStatusCode.NotFound, "Not found", Capabilities.Absent);

        Assert.Equal(missing, await Asked("/t/studio/plans/" + hidden.Id + "/edit", JsonBody(new { body = "x" })));
        Assert.Equal(missing, await Asked("/t/studio/plans/" + Tids.Mint() + "/edit", JsonBody(new { body = "x" })));
        Assert.Equal(missing, await Asked("/t/studio/plans/" + hidden.Id + "/edit", Array()));
        Assert.Equal(missing, await Asked("/t/studio/plans", Array()));
        Assert.Equal(missing, await Asked("/t/studio/nothing", Array()));
        Assert.Equal(missing, await Asked("/t/studio/new", Array()));
        Assert.Equal(missing, await Asked("/t/nowhere/new", Array()));
        // An address no Tangent has, and one whose slug no Tangent uses yet, answer as the hidden one does.
        var unused = await Send(ada.Client, HttpMethod.Get, "/t/unused-slug/new", json: true);
        var nowhere = await Send(ada.Client, HttpMethod.Get, "/t/nowhere/new", json: true);
        var studio = await Send(ada.Client, HttpMethod.Get, "/t/studio/new", json: true);
        Assert.Equal(missing, (unused.Status, unused.Title, unused.Gate));
        Assert.Equal((unused.Status, unused.Body), (nowhere.Status, nowhere.Body));
        Assert.Equal((unused.Status, unused.Body), (studio.Status, studio.Body));
        Assert.Equal(before, await Counted());
    }

    [Fact]
    public async Task On_a_private_place_a_visitor_reads_the_same_sign_in_for_every_address_whether_it_holds_something_or_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", Audience.Private, ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
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
            (HttpMethod.Post, PostPath(root, root) + "/edit", JsonBody(new { body = "x", version = 1 })),
            (HttpMethod.Post, TopicPath(root) + "/" + Tids.Mint() + "/remove", JsonBody(new { })),
            (HttpMethod.Post, PostPath(root, root) + "/remove", JsonBody(new { })),
            (HttpMethod.Post, TopicPath(root), JsonBody(new { id = Tids.Mint(), body = "x" })),
            (HttpMethod.Post, "/t/workshop/nothing", JsonBody(new { id = Tids.Mint(), body = "x" })),
            (HttpMethod.Post, "/t/workshop/new", JsonBody(new { id = Tids.Mint(), title = "x", body = "x" })),
            (HttpMethod.Post, "/t/nowhere/new", JsonBody(new { id = Tids.Mint(), title = "x", body = "x" }))
        };
        var answers = new List<(HttpStatusCode, string?, string?, string)>();
        foreach (var (method, path, content) in addresses)
        {
            var answered = await Send(visitor, method, path, content, json: true);
            answers.Add((answered.Status, answered.Title, answered.Gate, answered.Body));
        }

        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (answers[0].Item1, answers[0].Item3));
        Assert.All(answers, answer => Assert.Equal(answers[0], answer));
        Assert.Equal(before, await Counted());
    }

    [Fact]
    public async Task A_removal_whose_body_passes_the_cap_is_refused_and_removes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        using var ada = await SignedIn(tangentSpace, Ada);
        var fields = await ada.Token(PostPath(root, root) + "/edit", Cancellation);
        fields["padding"] = new string('x', 3 * 1024 * 1024);

        var answered = await Send(ada.Client, HttpMethod.Post, PostPath(root, root) + "/remove", FormBody(fields), json: true);

        // The form token is read through the capped body, so the request is refused as a form whose token cannot be read.
        Assert.Equal((HttpStatusCode.BadRequest, "Open the form again"), (answered.Status, answered.Title));
        Assert.False((await Post.Get(root.Id, Cancellation))?.Removed);
        Assert.Empty(await Events.Event.Query(e => e.Kind == Events.EventKind.PostRemoved, Cancellation));
    }
}
