using System.Diagnostics;
using System.Net;
using Koan.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Writer;
using Xunit;

namespace Tangent.Participants;

/// <summary>The profile job: one bounded public read, cached on the participant through the writer, at most once a
/// day.</summary>
public sealed class ProfileJobTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_job_caches_handle_display_name_and_avatar_through_a_command_and_a_run_within_a_day_is_skipped()
    {
        await using var stub = await ProfileStub.StartAsync(Cancellation);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?>
        {
            ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
            ["Tangent:Profile:BaseAddress"] = stub.Answering.AbsoluteUri
        }, ct: Cancellation);
        var committed = new List<Committed>();
        tangentSpace.Services.GetRequiredService<WriteLoop>().Committed += commit => { lock (committed) committed.Add(commit); };
        using var browser = new Browser(tangentSpace);
        const string did = "did:plc:ada4example";

        // The first arrival submits the job.
        using var arriving = await browser.SignIn("ada", did, Cancellation, handle: "ada.example");
        using var arrived = await browser.Arrive("person", Cancellation);
        Assert.Equal(HttpStatusCode.SeeOther, arrived.StatusCode);
        var participant = await Captured(did);

        // The handle is the one the connector verified, never the profile service's.
        Assert.Equal("ada.example", participant.Handle);
        Assert.Equal((ProfileStub.DisplayName, ProfileStub.Avatar), (participant.Profile.DisplayName, participant.Profile.Avatar));
        Assert.Equal("Writes things.", participant.Profile.Description);
        lock (committed) Assert.Contains(committed, commit => commit.Command is CaptureProfile);

        // Within the day: another run reads nothing, and a sign-in submits nothing.
        var again = await participant.Job.Submit(ct: Cancellation);
        Assert.Equal(JobStatus.Completed, (await again.Completion(Bound, Cancellation)).Status);
        using var signedInAgain = await browser.SignIn("ada", did, Cancellation, handle: "ada.example");
        Assert.Equal(HttpStatusCode.OK, signedInAgain.StatusCode);

        Assert.Equal([did], stub.Requests);
        Assert.Equal(participant.Profile.CapturedAt, (await Participant.Get(did, Cancellation))!.Profile.CapturedAt);
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
        var stored = new Participant { Id = did, Handle = "old.example", Profile = new Profile { CapturedAt = stale } };
        Assert.IsType<Outcome<Participant>.Done>(
            await tangentSpace.Services.GetRequiredService<WriteLoop>().Send(new Store<Participant>(stored), Cancellation));
        using var browser = new Browser(tangentSpace);

        using var signedIn = await browser.SignIn("stale", did, Cancellation, handle: "new.example");
        var participant = await Captured(did, after: stale);

        Assert.Equal("/", signedIn.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal("new.example", participant.Handle);
        Assert.Equal([did], stub.Requests);
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
        var stored = new Participant { Id = did, Handle = "before.example", Profile = new Profile { DisplayName = "Before" } };
        Assert.IsType<Outcome<Participant>.Done>(
            await tangentSpace.Services.GetRequiredService<WriteLoop>().Send(new Store<Participant>(stored), Cancellation));

        var reading = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<ProfileReadFailed>(() =>
            tangentSpace.Services.GetRequiredService<ProfileReader>().Read(did, Cancellation));
        reading.Stop();
        var job = await stored.Job.Submit(ct: Cancellation);
        var outcome = await job.Completion(Bound, Cancellation);

        Assert.Contains(reason, failure.Message);
        Assert.True(reading.Elapsed < TimeSpan.FromSeconds(5), $"The read took {reading.Elapsed}.");
        Assert.Equal(JobStatus.Completed, outcome.Status);
        Assert.Equal([did, did], stub.Requests);
        var after = await Participant.Get(did, Cancellation);
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
            if (await Participant.Get(did, Cancellation) is { Profile.CapturedAt: { } captured } participant
                && (after is null || captured > after)) return participant;
            await Task.Delay(100, Cancellation);
        }
        throw new TimeoutException($"The profile of {did} was not captured within {Bound}.");
    }
}
