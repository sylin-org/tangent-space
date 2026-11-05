using System.Net;
using System.Text;
using Koan.Data.Core.Commands;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Connections;

/// <summary>Once an article goes out, what the network says about it comes back under it, attributed and never a Post of
/// the place: its Bluesky post's counts and the replies and quotes there, the same for a visitor and an agent — against a
/// stand-in for the public AppView and a repository double, never anyone's real account.</summary>
public sealed class ElsewhereTests
{
    [Fact]
    public async Task A_published_articles_likes_reposts_replies_and_quotes_come_back_under_it()
    {
        var network = new Network();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), services =>
        {
            services.AddSingleton<IAtprotoRepository>(new PublishTests.Repository());
            services.AddHttpClient<AppView>().ConfigurePrimaryHttpMessageHandler(() => network);
        }, Cancellation);
        await Place(tangentSpace);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.ProprietorId = Ada;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
        var article = await Started(Ada, "Mending with gold", "A cracked bowl tells you where it was weak.", Articles);
        var page = TopicPath(article);
        using var ada = await SignedIn(tangentSpace, Ada);
        await Form(ada, page, page + "/" + PublishController.Segment, []);

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        bound.CancelAfter(TimeSpan.FromSeconds(20));
        while ((await Elsewhere.Get(article.Id, bound.Token))?.Likes is not 128) await Task.Delay(100, bound.Token);
        Assert.Equal($"at://{Ada}/{AtprotoAdapter.Notes}/{article.Id}", network.Asked);

        var html = (await Send(Agent(tangentSpace), HttpMethod.Get, page)).Body;
        Assert.All(["128 likes", "17 reposts", "1 quote ", "9 replies"], count => Assert.Contains(count, html));
        Assert.Contains("Mira Okafor", html);
        Assert.Contains("https://bsky.app/profile/did:plc:mira/post/3reply", html);
        Assert.Contains("quoted", html);

        var json = (await Send(Agent(tangentSpace), HttpMethod.Get, page, json: true)).Body;
        Assert.Contains("\"likes\":128", json);
        Assert.Contains("\"handle\":\"tomas.example\"", json);
        Assert.Contains("\"replyCount\":0", json);
    }

    /// <summary>The public AppView as it answers one Bluesky post: its thread with one reply, and one quote.</summary>
    private sealed class Network : HttpMessageHandler
    {
        public string? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            Asked = query["uri"];
            var body = request.RequestUri.AbsolutePath.EndsWith("app.bsky.feed.getPostThread", StringComparison.Ordinal)
                ? """
                  {"thread":{"$type":"app.bsky.feed.defs#threadViewPost","post":{"uri":"x","likeCount":128,"repostCount":17,"quoteCount":1,"replyCount":9},
                   "replies":[{"post":{"uri":"at://did:plc:mira/app.bsky.feed.post/3reply","author":{"did":"did:plc:mira","handle":"mira.example","displayName":"Mira Okafor"},
                     "record":{"text":"This is the push I needed to mend my grandmother's teapot.","createdAt":"2026-11-01T10:00:00.000Z"}}}]}}
                  """
                : """
                  {"posts":[{"uri":"at://did:plc:tomas/app.bsky.feed.post/3quote","author":{"did":"did:plc:tomas","handle":"tomas.example"},
                    "record":{"text":"A crack as the most honest part of an object.","createdAt":"2026-11-01T11:00:00.000Z"}}]}
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
