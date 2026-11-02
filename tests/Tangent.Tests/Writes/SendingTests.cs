using System.Net;
using System.Net.Http.Headers;
using Koan.Data.Core.Commands;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>What a person's words meet behind a busy store: they finish whatever becomes of the request that sent them,
/// and told they never ran, they are safe to send again.</summary>
public sealed class SendingTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_reply_whose_request_ends_behind_a_busy_store_still_lands()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var id = Tids.Mint();
        using var request = await Reply(ada, root, id, "From the porch beam.");

        var release = new TaskCompletionSource();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new Holding(release.Task, running).Send(Cancellation);
        await running.Task.WaitAsync(Bound, Cancellation);

        using (var closing = CancellationTokenSource.CreateLinkedTokenSource(Cancellation))
        {
            var sending = ada.Client.SendAsync(request, closing.Token);
            // Long enough for the request to reach the store's turn, which reads do not wait for.
            await Task.Delay(TimeSpan.FromSeconds(1.5), Cancellation);
            await closing.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        }

        release.SetResult();
        await held.WaitAsync(Bound, Cancellation);

        Post? landed = null;
        for (var tries = 0; tries < 100 && landed is null; tries++)
        {
            landed = await Post.Get(id, Cancellation);
            if (landed is null) await Task.Delay(100, Cancellation);
        }

        Assert.Equal("From the porch beam.", landed?.Body);
    }

    [Fact]
    public async Task A_reply_told_it_never_ran_says_so_and_sent_again_it_lands()
    {
        var settings = Settings();
        settings["Koan:Data:Sqlite:BusyTimeout"] = "00:00:01";
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(settings, ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var id = Tids.Mint();

        var release = new TaskCompletionSource();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new Holding(release.Task, running).Send(Cancellation);
        await running.Task.WaitAsync(Bound, Cancellation);
        using var busy = await ada.Client.SendAsync(await Reply(ada, root, id, "From the porch beam."), Cancellation);
        release.SetResult();
        await held.WaitAsync(Bound, Cancellation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        Assert.Equal("1", busy.Headers.RetryAfter?.ToString());
        Assert.Null(await Post.Get(id, Cancellation));

        using var again = await ada.Client.SendAsync(await Reply(ada, root, id, "From the porch beam."), Cancellation);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal("From the porch beam.", (await Post.Get(id, Cancellation))?.Body);
    }

    /// <summary>The request a composer sends for a reply, as JSON, with the page's token.</summary>
    private static async Task<HttpRequestMessage> Reply(Participants.Browser browser, Post root, string id, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, TopicPath(root)) { Content = JsonBody(new { id, body }) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add(TokenHeader, (await browser.Token(TopicPath(root), Cancellation)).Values.Single());
        return request;
    }

    /// <summary>Keeps the store until <paramref name="Until"/> completes, saying when it began through
    /// <paramref name="Running"/>; writes nothing.</summary>
    private sealed record Holding(Task Until, TaskCompletionSource Running) : ICommand<bool>
    {
        public async Task<Outcome<bool>> Run(CancellationToken ct)
        {
            Running.SetResult();
            await Until.WaitAsync(ct);
            return new Outcome<bool>.Done(true);
        }
    }
}
