using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Tangent.Participants;
using Xunit;

namespace Tangent.Connections;

/// <summary>What the author hid on Bluesky stays out from under their article here: a reply its post's gate lists, a
/// quote they detached, and whatever Bluesky labels to hide or take down, the post or its account; so does every post of
/// an account that asks apps to show it to no one signed out — read from the public AppView's answers as it gives them,
/// against a stand-in that answers them.</summary>
public sealed class AppViewTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string Note = "at://did:plc:ada4example/app.bsky.feed.post/3m4note";

    [Fact]
    public async Task A_reply_the_author_hid_a_quote_they_detached_a_post_labelled_hidden_or_taken_down_and_an_account_asking_not_to_be_shown_signed_out_stay_out()
    {
        var appView = new AppView(new HttpClient(new Recorded()),
            Options.Create(new ProfileReaderOptions { BaseAddress = new Uri("https://appview.example/") }));

        var heard = await appView.Read(Note, Cancellation);

        Assert.Equal(
            [
                (Reference.Quote, "tomas.example", "A crack as the most honest part of an object."),
                (Reference.Reply, "june.example", "Do you use urushi or the epoxy kits?"),
                (Reference.Reply, "mira.example", "This is the push I needed to mend my grandmother's teapot.")
            ],
            heard.References.Select(reference => (reference.Kind, reference.Handle, reference.Text)));
        // The counts stay the network's own.
        Assert.Equal((12, 2, 4, 6), (heard.Likes, heard.Reposts, heard.Quotes, heard.Replies));
    }

    /// <summary>The public AppView's answers about one Bluesky post, in the shapes it gives them: its replies, one of them
    /// listed by its gate as hidden by its author, and its quotes, two of them detached by its author.</summary>
    private sealed class Recorded : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(Note, System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["uri"]);
            var body = request.RequestUri.AbsolutePath.EndsWith("app.bsky.feed.getPostThread", StringComparison.Ordinal) ? Replies : Quotes;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }

        private const string Replies = """
            {"thread":{"$type":"app.bsky.feed.defs#threadViewPost",
              "post":{"uri":"at://did:plc:ada4example/app.bsky.feed.post/3m4note","cid":"bafyreinote",
                "author":{"did":"did:plc:ada4example","handle":"ada.example","displayName":"Ada","labels":[]},
                "record":{"$type":"app.bsky.feed.post","text":"Mending with gold","createdAt":"2026-11-01T09:00:00.000Z"},
                "replyCount":6,"repostCount":2,"likeCount":12,"quoteCount":4,"indexedAt":"2026-11-01T09:00:01.000Z","labels":[],
                "threadgate":{"uri":"at://did:plc:ada4example/app.bsky.feed.threadgate/3m4note","cid":"bafyreigate",
                  "record":{"$type":"app.bsky.feed.threadgate","post":"at://did:plc:ada4example/app.bsky.feed.post/3m4note",
                    "hiddenReplies":["at://did:plc:pell4example/app.bsky.feed.post/3m4abuse"],"createdAt":"2026-11-01T12:00:00.000Z"},"lists":[]}},
              "replies":[
                {"$type":"app.bsky.feed.defs#threadViewPost","replies":[],
                 "post":{"uri":"at://did:plc:mira4example/app.bsky.feed.post/3m4mira","cid":"bafyreimira",
                   "author":{"did":"did:plc:mira4example","handle":"mira.example","displayName":"Mira Okafor","labels":[]},
                   "record":{"$type":"app.bsky.feed.post","text":"This is the push I needed to mend my grandmother's teapot.","createdAt":"2026-11-01T10:00:00.000Z"},
                   "indexedAt":"2026-11-01T10:00:01.000Z","labels":[]}},
                {"$type":"app.bsky.feed.defs#threadViewPost","replies":[],
                 "post":{"uri":"at://did:plc:pell4example/app.bsky.feed.post/3m4abuse","cid":"bafyreiabuse",
                   "author":{"did":"did:plc:pell4example","handle":"pell.example","labels":[]},
                   "record":{"$type":"app.bsky.feed.post","text":"Something cruel.","createdAt":"2026-11-01T11:00:00.000Z"},
                   "indexedAt":"2026-11-01T11:00:01.000Z","labels":[]}},
                {"$type":"app.bsky.feed.defs#threadViewPost","replies":[],
                 "post":{"uri":"at://did:plc:spam4example/app.bsky.feed.post/3m4spam","cid":"bafyreispam",
                   "author":{"did":"did:plc:spam4example","handle":"spam.example","labels":[]},
                   "record":{"$type":"app.bsky.feed.post","text":"Cheap gold, click here.","createdAt":"2026-11-01T11:10:00.000Z"},
                   "indexedAt":"2026-11-01T11:10:01.000Z",
                   "labels":[{"src":"did:plc:ar7c4by46qjdydhdevvrndac","uri":"at://did:plc:spam4example/app.bsky.feed.post/3m4spam","cid":"bafyreispam","val":"!hide","cts":"2026-11-01T11:20:00.000Z"}]}},
                {"$type":"app.bsky.feed.defs#threadViewPost","replies":[],
                 "post":{"uri":"at://did:plc:gone4example/app.bsky.feed.post/3m4gone","cid":"bafyreigone",
                   "author":{"did":"did:plc:gone4example","handle":"gone.example",
                     "labels":[{"src":"did:plc:ar7c4by46qjdydhdevvrndac","uri":"did:plc:gone4example","val":"!takedown","cts":"2026-11-01T11:30:00.000Z"}]},
                   "record":{"$type":"app.bsky.feed.post","text":"From an account taken down.","createdAt":"2026-11-01T11:15:00.000Z"},
                   "indexedAt":"2026-11-01T11:15:01.000Z","labels":[]}},
                {"$type":"app.bsky.feed.defs#threadViewPost","replies":[],
                 "post":{"uri":"at://did:plc:june4example/app.bsky.feed.post/3m4june","cid":"bafyreijune",
                   "author":{"did":"did:plc:june4example","handle":"june.example","labels":[]},
                   "record":{"$type":"app.bsky.feed.post","text":"Do you use urushi or the epoxy kits?","createdAt":"2026-11-01T11:40:00.000Z"},
                   "indexedAt":"2026-11-01T11:40:01.000Z",
                   "labels":[{"src":"did:plc:ar7c4by46qjdydhdevvrndac","uri":"at://did:plc:june4example/app.bsky.feed.post/3m4june","val":"!hide","neg":true,"cts":"2026-11-01T11:50:00.000Z"}]}},
                {"$type":"app.bsky.feed.defs#threadViewPost","replies":[],
                 "post":{"uri":"at://did:plc:noor4example/app.bsky.feed.post/3m4noor","cid":"bafyreinoor",
                   "author":{"did":"did:plc:noor4example","handle":"noor.example","displayName":"Noor",
                     "labels":[{"src":"did:plc:noor4example","uri":"at://did:plc:noor4example/app.bsky.actor.profile/self","cid":"bafyreiprofile","val":"!no-unauthenticated","cts":"2026-10-01T08:00:00.000Z"}]},
                   "record":{"$type":"app.bsky.feed.post","text":"Only for people signed in.","createdAt":"2026-11-01T11:45:00.000Z"},
                   "indexedAt":"2026-11-01T11:45:01.000Z","labels":[]}},
                {"$type":"app.bsky.feed.defs#blockedPost","uri":"at://did:plc:kai4example/app.bsky.feed.post/3m4kai","blocked":true,
                 "author":{"did":"did:plc:kai4example"}}]},
             "threadgate":{"uri":"at://did:plc:ada4example/app.bsky.feed.threadgate/3m4note","cid":"bafyreigate",
               "record":{"$type":"app.bsky.feed.threadgate","post":"at://did:plc:ada4example/app.bsky.feed.post/3m4note",
                 "hiddenReplies":["at://did:plc:pell4example/app.bsky.feed.post/3m4abuse"],"createdAt":"2026-11-01T12:00:00.000Z"},"lists":[]}}
            """;

        private const string Quotes = """
            {"uri":"at://did:plc:ada4example/app.bsky.feed.post/3m4note","posts":[
              {"uri":"at://did:plc:tomas4example/app.bsky.feed.post/3m4tomas","cid":"bafyreitomas",
               "author":{"did":"did:plc:tomas4example","handle":"tomas.example","displayName":"Tomás","labels":[]},
               "record":{"$type":"app.bsky.feed.post","text":"A crack as the most honest part of an object.","createdAt":"2026-11-01T13:00:00.000Z",
                 "embed":{"$type":"app.bsky.embed.record","record":{"uri":"at://did:plc:ada4example/app.bsky.feed.post/3m4note","cid":"bafyreinote"}}},
               "embed":{"$type":"app.bsky.embed.record#view","record":{"$type":"app.bsky.embed.record#viewRecord",
                 "uri":"at://did:plc:ada4example/app.bsky.feed.post/3m4note","cid":"bafyreinote",
                 "author":{"did":"did:plc:ada4example","handle":"ada.example"},"value":{"text":"Mending with gold"},"indexedAt":"2026-11-01T09:00:01.000Z"}},
               "indexedAt":"2026-11-01T13:00:01.000Z","labels":[]},
              {"uri":"at://did:plc:pell4example/app.bsky.feed.post/3m4sneer","cid":"bafyreisneer",
               "author":{"did":"did:plc:pell4example","handle":"pell.example","labels":[]},
               "record":{"$type":"app.bsky.feed.post","text":"Look at this nonsense.","createdAt":"2026-11-01T13:10:00.000Z",
                 "embed":{"$type":"app.bsky.embed.record","record":{"uri":"at://did:plc:ada4example/app.bsky.feed.post/3m4note","cid":"bafyreinote"}}},
               "embed":{"$type":"app.bsky.embed.record#view","record":{"$type":"app.bsky.embed.record#viewDetached",
                 "uri":"at://did:plc:ada4example/app.bsky.feed.post/3m4note","detached":true}},
               "indexedAt":"2026-11-01T13:10:01.000Z","labels":[]},
              {"uri":"at://did:plc:pell4example/app.bsky.feed.post/3m4jeer","cid":"bafyreijeer",
               "author":{"did":"did:plc:pell4example","handle":"pell.example","labels":[]},
               "record":{"$type":"app.bsky.feed.post","text":"And a picture of it.","createdAt":"2026-11-01T13:20:00.000Z"},
               "embed":{"$type":"app.bsky.embed.recordWithMedia#view",
                 "record":{"record":{"$type":"app.bsky.embed.record#viewDetached","uri":"at://did:plc:ada4example/app.bsky.feed.post/3m4note","detached":true}},
                 "media":{"$type":"app.bsky.embed.images#view","images":[]}},
               "indexedAt":"2026-11-01T13:20:01.000Z","labels":[]},
              {"uri":"at://did:plc:noor4example/app.bsky.feed.post/3m4quote","cid":"bafyreinoorquote",
               "author":{"did":"did:plc:noor4example","handle":"noor.example","displayName":"Noor",
                 "labels":[{"src":"did:plc:noor4example","uri":"at://did:plc:noor4example/app.bsky.actor.profile/self","cid":"bafyreiprofile","val":"!no-unauthenticated","cts":"2026-10-01T08:00:00.000Z"}]},
               "record":{"$type":"app.bsky.feed.post","text":"Quoted for people signed in.","createdAt":"2026-11-01T13:25:00.000Z"},
               "embed":{"$type":"app.bsky.embed.record#view","record":{"$type":"app.bsky.embed.record#viewRecord",
                 "uri":"at://did:plc:ada4example/app.bsky.feed.post/3m4note","cid":"bafyreinote"}},
               "indexedAt":"2026-11-01T13:25:01.000Z","labels":[]},
              {"uri":"at://did:plc:spam4example/app.bsky.feed.post/3m4ad","cid":"bafyreiad",
               "author":{"did":"did:plc:spam4example","handle":"spam.example","labels":[]},
               "record":{"$type":"app.bsky.feed.post","text":"Gold, cheap.","createdAt":"2026-11-01T13:30:00.000Z"},
               "embed":{"$type":"app.bsky.embed.record#view","record":{"$type":"app.bsky.embed.record#viewRecord",
                 "uri":"at://did:plc:ada4example/app.bsky.feed.post/3m4note","cid":"bafyreinote"}},
               "indexedAt":"2026-11-01T13:30:01.000Z",
               "labels":[{"src":"did:plc:ar7c4by46qjdydhdevvrndac","uri":"at://did:plc:spam4example/app.bsky.feed.post/3m4ad","val":"!takedown","cts":"2026-11-01T13:40:00.000Z"}]}]}
            """;
    }
}
