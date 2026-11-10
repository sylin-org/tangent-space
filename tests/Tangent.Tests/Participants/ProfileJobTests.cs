using System.Diagnostics;
using System.Net;
using Koan.Data.Core.Commands;
using Koan.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tangent.Participants;

/// <summary>The profile job: one bounded public read, cached on the participant through a command, at most once in the
/// profile's lifetime, at a sign-in or a Post they write.</summary>
public sealed class ProfileJobTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_job_caches_handle_display_name_and_avatar_through_a_command_and_a_run_within_its_lifetime_is_skipped()
    {
        await using var stub = await ProfileStub.StartAsync(Cancellation);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?>
        {
            ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
            ["Tangent:Profile:BaseAddress"] = stub.Answering.AbsoluteUri
        }, ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        const string did = "did:plc:ada4example";

        // The first sign-in is the first arrival, and submits the job.
        using var arriving = await browser.SignIn("ada", did, Cancellation, handle: "ada.example");
        Assert.Equal(HttpStatusCode.OK, arriving.StatusCode);
        var participant = await Captured(did);

        // The handle is the one the connector verified, never the profile service's.
        Assert.Equal("ada.example", participant.Handle);
        Assert.Equal((ProfileStub.DisplayName, ProfileStub.Avatar), (participant.Profile.DisplayName, participant.Profile.Avatar));
        Assert.Equal("Writes things.", participant.Profile.Description);
        // No bot self-label: a person.
        Assert.Equal((ParticipantKind.Person, KindSource.ProfileLabel), (participant.Kind, participant.KindSource));

        // Within its lifetime: another run reads nothing, and a sign-in submits nothing.
        var again = await participant.Job.Submit(ct: Cancellation);
        Assert.True((await again.Completion(Bound, Cancellation)).Succeeded);
        using var signedInAgain = await browser.SignIn("ada", did, Cancellation, handle: "ada.example");
        Assert.Equal(HttpStatusCode.OK, signedInAgain.StatusCode);

        Assert.Equal([did], stub.Requests);
        Assert.Equal(participant.Profile.CapturedAt, (await Persona.Arrived(did, Cancellation))!.Profile.CapturedAt);
    }

    [Fact]
    public async Task A_sign_in_after_the_profiles_lifetime_submits_the_job_again()
    {
        await using var stub = await ProfileStub.StartAsync(Cancellation);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?>
        {
            ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
            ["Tangent:Profile:BaseAddress"] = stub.Answering.AbsoluteUri
        }, ct: Cancellation);
        const string did = "did:plc:stale4example";
        var stale = DateTimeOffset.UtcNow - Profile.Lifetime - TimeSpan.FromMinutes(1);
        var stored = new Participant { Id = Persona.Id(did.Split(':')[^1]), Did = did, Handle = "old.example", Profile = new Profile { CapturedAt = stale } };
        Assert.IsType<Outcome<Participant>.Done>(
            await new Store<Participant>(stored).Send(Cancellation));
        using var browser = new Browser(tangentSpace);

        using var signedIn = await browser.SignIn("stale", stored.Id, Cancellation, handle: "new.example");
        var participant = await Captured(did, after: stale);

        Assert.Equal("/", signedIn.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal("new.example", participant.Handle);
        Assert.Equal([did], stub.Requests);
    }

    [Theory]
    [InlineData("reply")]
    [InlineData("start_topic")]
    [InlineData("edit")]
    public async Task A_post_written_after_the_profiles_lifetime_reads_it_again_so_a_name_changed_on_Bluesky_follows_while_they_stay_signed_in(string write)
    {
        await using var stub = await ProfileStub.StartAsync(Cancellation);
        var settings = Posts.Writing.Settings();
        settings["Tangent:Profile:BaseAddress"] = stub.Answering.AbsoluteUri;
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(settings, ct: Cancellation);
        await Posts.Writing.Place(tangentSpace);
        var ada = Posts.Writing.Ada;
        var did = Persona.Did(ada);
        var root = await Posts.Writing.Started(ada, "Lanterns", "How do you hang yours?");
        using var session = await Posts.Writing.SignedIn(tangentSpace, ada);
        var signedIn = await Captured(did);

        // An hour on, the profile they signed in with, as Ada Before, is past its lifetime, and their account says Ada Lovelace now.
        var stale = DateTimeOffset.UtcNow - Profile.Lifetime - TimeSpan.FromMinutes(1);
        signedIn.Profile = new Profile { DisplayName = "Ada Before", CapturedAt = stale };
        Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(signedIn).Send(Cancellation));

        // A reply a page sends, a Topic an agent's tool starts, an edit sent as JSON: each answers as it did, and reads
        // the profile again.
        var path = Posts.Writing.TopicPath(root);
        switch (write)
        {
            case "reply":
                var replied = await Posts.Writing.Form(session, path, path, new Dictionary<string, string> { ["id"] = Posts.Tids.Mint(), ["body"] = "From the porch beam." });
                Assert.Equal(HttpStatusCode.SeeOther, replied.Status);
                break;
            case "start_topic":
                using (var tools = await Tools.ToolClient.Connect(tangentSpace, session))
                    Assert.False((await tools.Call("start_topic", new { tangent = Posts.Writing.Workshop, title = "Wicks", body = "Cotton or hemp?" })).Failed);
                break;
            default:
                var edit = Posts.Writing.PostPath(root, root) + "/edit";
                var edited = await Posts.Writing.Json(session, edit, HttpMethod.Patch, Posts.Writing.PostPath(root, root), new { title = "Lanterns", body = "How do you hang yours, and where?", version = 1 });
                Assert.Equal(HttpStatusCode.OK, edited.Status);
                break;
        }

        var after = await Captured(did, after: stale);
        Assert.Equal(ProfileStub.DisplayName, after.ShownName());
        Assert.Equal(2, stub.Requests.Count(asked => asked == did));
    }

    [Theory]
    [InlineData(KindSource.ProfileLabel, ParticipantKind.Person)]
    [InlineData(KindSource.OwnChoice, ParticipantKind.Agent)]
    public async Task An_agent_self_label_on_the_profile_makes_the_participant_an_agent_by_the_label_through_the_jobs_command_whatever_they_chose(
        KindSource source, ParticipantKind kind)
    {
        await using var stub = await ProfileStub.StartAsync(Cancellation);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Tangent:Profile:BaseAddress"] = stub.AgentLabelled.AbsoluteUri }, ct: Cancellation);
        const string did = "did:plc:lumen4example";
        var stored = new Participant { Id = Persona.Id(did.Split(':')[^1]), Did = did, Handle = "lumen.example", Kind = kind, KindSource = source };
        Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(stored).Send(Cancellation));

        var job = await stored.Job.Submit(ct: Cancellation);
        Assert.True((await job.Completion(Bound, Cancellation)).Succeeded);
        var after = await Persona.Arrived(did, Cancellation);

        Assert.NotNull(after);
        Assert.NotNull(after.Profile.CapturedAt);
        Assert.Equal((ParticipantKind.Agent, KindSource.ProfileLabel), (after.Kind, after.KindSource));
        Assert.True(after.Labelled);
    }

    [Theory]
    [InlineData("stall", "did not answer")]
    [InlineData("overflow", "is larger than 4096 bytes")]
    public async Task The_read_honours_its_timeout_and_its_byte_bound_and_leaves_the_cache_untouched(string behaviour, string reason)
    {
        await using var stub = await ProfileStub.StartAsync(Cancellation);
        var address = behaviour == "stall" ? stub.Stalling : stub.Overflowing;
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?>
        {
            ["Tangent:Profile:BaseAddress"] = address.AbsoluteUri,
            ["Tangent:Profile:Timeout"] = "00:00:01",
            ["Tangent:Profile:MaximumResponseBytes"] = "4096"
        }, ct: Cancellation);
        const string did = "did:plc:bounded4example";
        var stored = new Participant { Id = Persona.Id(did.Split(':')[^1]), Did = did, Handle = "before.example", Profile = new Profile { DisplayName = "Before" } };
        Assert.IsType<Outcome<Participant>.Done>(
            await new Store<Participant>(stored).Send(Cancellation));

        var reading = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<ProfileReadFailed>(() =>
            tangentSpace.Services.GetRequiredService<ProfileReader>().Read(did, Cancellation));
        reading.Stop();
        var job = await stored.Job.Submit(ct: Cancellation);
        var outcome = await job.Completion(Bound, Cancellation);

        Assert.Contains(reason, failure.Message);
        Assert.True(reading.Elapsed < TimeSpan.FromSeconds(5), $"The read took {reading.Elapsed}.");
        Assert.True(outcome.Succeeded);
        Assert.Equal([did, did], stub.Requests);
        var after = await Persona.Arrived(did, Cancellation);
        Assert.NotNull(after);
        Assert.Equal(("before.example", "Before", (DateTimeOffset?)null), (after.Handle, after.Profile.DisplayName, after.Profile.CapturedAt));
    }

    [Fact]
    public async Task An_avatar_is_kept_only_at_an_https_address()
    {
        await using var stub = await ProfileStub.StartAsync(Cancellation);
        await using var https = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Tangent:Profile:BaseAddress"] = stub.Answering.AbsoluteUri }, ct: Cancellation);
        var secure = await https.Services.GetRequiredService<ProfileReader>().Read("did:plc:secure4example", Cancellation);
        await using var plain = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Tangent:Profile:BaseAddress"] = stub.PlainAvatar.AbsoluteUri }, ct: Cancellation);
        var unsafeAvatar = await plain.Services.GetRequiredService<ProfileReader>().Read("did:plc:plain4example", Cancellation);

        Assert.Equal(ProfileStub.Avatar, secure.Avatar);
        Assert.Null(unsafeAvatar.Avatar);
        Assert.Equal(ProfileStub.DisplayName, unsafeAvatar.DisplayName);
    }

    /// <summary>The participant once the job has cached its profile, waiting no longer than the bound.</summary>
    private static async Task<Participant> Captured(string did, DateTimeOffset? after = null)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < Bound)
        {
            if (await Persona.Arrived(did, Cancellation) is { Profile.CapturedAt: { } captured } participant
                && (after is null || captured > after)) return participant;
            await Task.Delay(100, Cancellation);
        }
        throw new TimeoutException($"The profile of {did} was not captured within {Bound}.");
    }
}
