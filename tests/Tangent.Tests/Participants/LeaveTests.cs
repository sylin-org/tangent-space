using System.Net;
using Koan.Data.Abstractions;
using Koan.Data.Core.Commands;
using Koan.Identity;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Connections;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Tools;
using Xunit;
using static Tangent.Posts.Writing;
using KoanPerson = Koan.Identity.Identity;

namespace Tangent.Participants;

/// <summary>Leaving the place: a participant on their own profile leaves, confirmed in the page, and lands on the door
/// signed out; their checkpoints, Connection, asks and row go, Koan erases their person with their roles, overwrites and
/// links, and their Posts stay, read as someone who left, linked out to the account each was written as, while who is
/// around and their profile leave them out. Signing in again arrives anew. The operator stays, and an agent leaves
/// through leave_place.</summary>
public sealed class LeaveTests
{
    private static readonly string AdaHandle = Persona.Handle(Ada);

    [Fact]
    public async Task A_participant_leaves_from_their_own_profile_and_everything_of_theirs_goes_but_their_posts_which_read_as_someone_who_left()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await roles.Overwrite(Overwrites.Resource(Studio), OverwriteSubjectKind.Person, Ada, [Bits.Token(Bit.View), Bits.Token(Bit.Post)], [], ct: Cancellation);
        var root = await Started(Ada, "Lanterns at dusk", "Who lights theirs first?");
        await Replied(root, Ada, "I do, at six.");
        await Replied(root, Bea, "Then I am second.");
        Assert.IsType<Outcome<Checkpoint>.Done>(await new Store<Checkpoint>(Checkpoint.At(Ada, Scope.Place, 2)).Send(Cancellation));
        Assert.IsType<Outcome<Checkpoint>.Done>(await new Store<Checkpoint>(Checkpoint.At(Ada, Scope.Topic(root.Id), 1)).Send(Cancellation));
        Assert.IsType<Outcome<Connection>.Done>(await new Store<Connection>(new Connection { Id = Ada, State = ConnectionState.Publishing, Since = DateTimeOffset.UtcNow }).Send(Cancellation));
        var receipt = new Receipt { Id = Receipt.Key(Ada, root.Id), ConnectionId = Ada, PostId = root.Id, TangentId = Workshop, At = DateTimeOffset.UtcNow };
        Assert.IsType<Outcome<Receipt>.Done>(await new Store<Receipt>(receipt).Send(Cancellation));
        Assert.IsType<Outcome<Ask>.Done>(await new Store<Ask>(new Ask { Id = Ask.Key(Studio, Ada), TangentId = Studio, PersonId = Ada, AskedAt = DateTimeOffset.UtcNow }).Send(Cancellation));

        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        var profile = "/p/" + AdaHandle;
        var own = await Send(ada.Client, HttpMethod.Get, profile);
        Assert.Contains("Leave this Tangent Space", own.Body);
        Assert.Contains("read as someone who left", own.Body);
        Assert.Contains("action=\"" + BaseUrl + PeopleController.LeavePath + "\"", own.Body);
        Assert.Contains(AdaHandle, (await Send(bea.Client, HttpMethod.Get, "/", json: true)).Body);
        // Someone else's profile offers no way to make them leave.
        Assert.DoesNotContain("Leave this Tangent Space", (await Send(bea.Client, HttpMethod.Get, profile)).Body);

        var left = await Form(ada, profile, PeopleController.LeavePath, new Dictionary<string, string>());

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/"), (left.Status, left.Location));
        Assert.False((await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json.TryGetProperty("who", out _));
        Assert.Null(await Participant.Get(Ada, Cancellation));
        Assert.Empty(await Checkpoint.Query(checkpoint => checkpoint.ParticipantId == Ada, Cancellation));
        Assert.Null(await Connection.Get(Ada, Cancellation));
        Assert.Null(await Ask.Get(Ask.Key(Studio, Ada), Cancellation));
        Assert.NotNull(await Receipt.Get(receipt.Id, Cancellation));
        Assert.False((await roles.Bag(Ada, authenticated: true, Cancellation)).InRole(Roles.Member));
        Assert.Empty((await roles.OverwritesOf(OverwriteSubjectKind.Person, Ada, ct: Cancellation)).Items);
        Assert.Null(await KoanPerson.Get(Ada, Cancellation));
        Assert.Empty(await ExternalIdentityLink.Query(link => link.IdentityId == Ada, Cancellation));
        Assert.Equal(3, (await Post.Query(post => post.TopicId == root.Id, Cancellation)).Count);

        // Their Posts read as someone who left, the name linking out to the account each was written as; no card, no profile.
        var page = PageContent((await Send(bea.Client, HttpMethod.Get, TopicPath(root))).Body);
        Assert.Contains(Render.Person.LeftName, page);
        Assert.Contains("href=\"" + Participant.Elsewhere(root.AuthorDid) + "\"", page);
        Assert.DoesNotContain("/p/" + AdaHandle, page);
        var json = (await Send(bea.Client, HttpMethod.Get, TopicPath(root), json: true)).Json;
        Assert.Equal((Render.Person.LeftName, root.AuthorDid), (json.GetProperty("root").GetProperty("author").GetProperty("name").GetString(),
            json.GetProperty("root").GetProperty("writtenAs").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(bea.Client, HttpMethod.Get, profile, json: true)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(bea.Client, HttpMethod.Get, "/p/" + Ada, json: true)).Status);
        var around = (await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("around");
        Assert.DoesNotContain(Ada, around.GetProperty("people").EnumerateArray().Select(person => person.GetProperty("id").GetString()));
        Assert.Equal(around.GetProperty("people").GetArrayLength(), around.GetProperty("count").GetInt32());

        // Signing in again with the same account arrives anew: a new person, a member, and none of the old Posts theirs.
        using var again = new Browser(tangentSpace);
        (await again.SignIn("ada4writes", root.AuthorDid, Cancellation, handle: AdaHandle)).Dispose();
        var anew = await Persona.Arrived(root.AuthorDid, Cancellation);
        Assert.NotNull(anew);
        Assert.NotEqual(Ada, anew.Id);
        Assert.True(anew.ArrivedSeq > 0);
        Assert.True((await roles.Bag(anew.Id, authenticated: true, Cancellation)).InRole(Roles.Member));
        Assert.False((await Capabilities.LoadRows(anew.Id, Workshop, Cancellation)).For(root).Holds(Bit.EditOwn));
        Assert.Contains(Render.Person.LeftName, PageContent((await Send(again.Client, HttpMethod.Get, TopicPath(root))).Body));
    }

    [Fact]
    public async Task The_operator_stays_with_the_place_and_an_agent_leaves_through_leave_place()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.OperatorId = Mo;
        space.ProprietorId = Mo;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
        using var mo = await SignedIn(tangentSpace, Mo);

        Assert.DoesNotContain("Leave this Tangent Space", (await Send(mo.Client, HttpMethod.Get, "/p/" + Persona.Handle(Mo))).Body);
        var refused = await Json(mo, "/p/" + Persona.Handle(Mo), PeopleController.LeavePath, new { });
        Assert.Equal((HttpStatusCode.Conflict, "operator"), (refused.Status, refused.Word));
        Assert.NotNull(await Participant.Get(Mo, Cancellation));
        Assert.True((await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json.TryGetProperty("who", out _));

        using var bea = await SignedIn(tangentSpace, Bea);
        using var agent = await ToolClient.Connect(tangentSpace, bea);
        var called = await agent.Call("leave_place");
        Assert.False(called.Failed, called.Text);
        Assert.Equal(BaseUrl + "/", called.Structured!.Value.GetProperty("door").GetString());
        Assert.Null(await Participant.Get(Bea, Cancellation));
        Assert.Null(await KoanPerson.Get(Bea, Cancellation));
    }
}
