using System.Net;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Posts;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Participants;

/// <summary>A participant is the place's own, bound to one Bluesky account at a time: using another keeps them, their
/// Posts, roles and standing, binds them to the new account, and each Post keeps the account it was written as, which its
/// author's card links out to from it; Koan's refusals of a binding are said on the page they land on.</summary>
public sealed class AccountTests
{
    private const string Earlier = "did:plc:ada4writes";
    private const string Later = "did:plc:ada4later";

    [Fact]
    public async Task Another_account_keeps_the_participant_their_posts_roles_and_standing_and_each_post_links_out_to_the_account_it_was_written_as()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        Assert.Equal(Earlier, (await Participant.Get(Ada, Cancellation))?.Did);
        var root = await Started(Ada, "Lanterns at dusk", "Who lights theirs first?");

        // The account menu carries the form, which posts same-site to Koan's binding and says what stays.
        var door = await Send(ada.Client, HttpMethod.Get, "/");
        Assert.Contains("action=\"" + BaseUrl + AnotherAccount.Route + "\"", door.Body);
        Assert.Contains("Use another Bluesky account", door.Body);
        Assert.Contains("Your earlier Posts stay linked to @ada4writes.example.", door.Body);

        using (var landed = await ada.UseAnotherAccount(Later, "ada.later.example", Cancellation, comeBack: TopicPath(root)))
            Assert.Equal((HttpStatusCode.OK, TopicPath(root)), (landed.StatusCode, landed.RequestMessage!.RequestUri!.AbsolutePath));

        var participant = await Participant.Get(Ada, Cancellation);
        Assert.Equal((Later, "ada.later.example", Standing.Admitted), (participant?.Did, participant?.Handle, participant?.Standing));
        Assert.Contains(Roles.Member, (await tangentSpace.Services.GetRequiredService<RoleCollection>().ForPerson(Ada, Cancellation)).Select(role => role.Key));
        Assert.Equal(3, await Participant.Count.Exact(Cancellation));

        // Her earlier Post keeps the earlier account; what she writes now is written as the new one.
        var reply = await Replied(root, Ada, "Whoever is first home.");
        Assert.Equal((Earlier, Later), ((await Post.Get(root.Id, Cancellation))?.AuthorDid, reply.AuthorDid));
        var page = await Send(ada.Client, HttpMethod.Get, TopicPath(root));
        Assert.Contains("data-account=\"" + Earlier + "\"", page.Body);
        Assert.Contains("data-account=\"" + Later + "\"", page.Body);
        var profile = await Send(ada.Client, HttpMethod.Get, "/p/" + Ada);
        Assert.Equal(HttpStatusCode.OK, profile.Status);
        Assert.Contains("data-elsewhere=\"https://bsky.app/profile/" + Later + "\"", profile.Body);
        Assert.Contains("data-mention=\"/p/" + Ada + "\"", profile.Body);

        // The earlier account, signing in again, arrives as a newcomer; nothing of hers goes with it.
        using var earlier = new Browser(tangentSpace);
        (await earlier.SignIn("ada", Earlier, Cancellation, handle: "ada4writes.example")).Dispose();
        var newcomer = await Persona.Arrived(Earlier, Cancellation);
        Assert.NotNull(newcomer);
        Assert.NotEqual(Ada, newcomer.Id);
        Assert.Equal(Ada, (await Post.Get(root.Id, Cancellation))?.AuthorId);
    }

    [Fact]
    public async Task An_account_that_is_someone_elses_here_stays_theirs_and_the_page_says_so()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var bea = await SignedIn(tangentSpace, Bea);
        using var ada = await SignedIn(tangentSpace, Ada);

        using var landed = await ada.UseAnotherAccount(Persona.Did(Bea), "bea4writes.example", Cancellation);

        Assert.Equal((HttpStatusCode.OK, "/settings/connections"), (landed.StatusCode, landed.RequestMessage!.RequestUri!.AbsolutePath));
        Assert.Contains("login_taken", landed.RequestMessage.RequestUri.Query);
        var body = await landed.Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("That account already signed in here as someone else, so it stays theirs. Nothing was merged.", body);
        Assert.Contains("<details class=\"another-account\" id=\"another\" open", body);
        Assert.Equal((Earlier, Persona.Did(Bea)), ((await Participant.Get(Ada, Cancellation))?.Did, (await Participant.Get(Bea, Cancellation))?.Did));
    }

    [Fact]
    public async Task A_sign_in_older_than_koan_asks_for_is_one_step_from_the_switch_it_wanted()
    {
        var clock = new Ahead();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), services => services.AddSingleton<TimeProvider>(clock), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        clock.By = TimeSpan.FromMinutes(11);

        using var landed = await ada.UseAnotherAccount(Later, "ada.later.example", Cancellation,
            failure: AnotherAccount.FailurePath + "&" + AnotherAccount.HandleQuery + "=ada.later.example");

        Assert.Equal((HttpStatusCode.OK, "/settings/connections"), (landed.StatusCode, landed.RequestMessage!.RequestUri!.AbsolutePath));
        var body = await landed.Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("Bluesky wants a fresh sign-in before you switch accounts.", body);
        // The one step: signing in again as the account she holds, which comes back to the form, open, with her handle kept.
        Assert.Contains("action=\"" + BaseUrl + SignInController.ChallengePath + "\"", body);
        Assert.Contains("name=\"identifier\" value=\"" + Earlier + "\"", body);
        Assert.Contains("name=\"return\" value=\"/settings/connections?another=1&amp;handle=ada.later.example\"", body);
        Assert.Contains("value=\"ada.later.example\"", body);
        Assert.Equal(Earlier, (await Participant.Get(Ada, Cancellation))?.Did);
    }

    /// <summary>The clock moved on by <see cref="By"/>.</summary>
    private sealed class Ahead : TimeProvider
    {
        public TimeSpan By { get; set; }

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + By;
    }
}
