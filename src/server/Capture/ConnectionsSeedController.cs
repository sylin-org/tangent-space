using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Mvc;
using Tangent.Connections;
using Tangent.Posts;

namespace Tangent.Capture;

/// <summary>The rows the Connections captures show: the article seed's place and articles, two more articles by the
/// persona, and her Connection publishing them on Bluesky — one updated, one published, and one it couldn't reach
/// Bluesky for, trying again — and what Bluesky said about the updated one. Compiled only into the capture host; fixed names, words and times.</summary>
public sealed class ConnectionsSeedController(RoleCollection roles) : ControllerBase
{
    private const string Persona = "did:plc:capture4persona";

    [HttpPost("/capture/seed/connections")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        await new ArticleSeedController(roles).Seed(ct);
        var gold = (await Post.Query(post => post.Slug == "mending-with-gold", ct)).Single();
        var leaf = await Write(new StartTopic(Tid(At(28, 10, 5), 8), "articles", "Gold leaf, not gold paint",
            "Leaf sits on the lacquer; paint sits on the surface. Only one of them is still there in ten years.", Persona, At: At(28, 10, 5)), ct);
        var bench = await Write(new StartTopic(Tid(At(30, 21, 40), 9), "articles", "The bench at night",
            "Most of the mending happens after the shop closes, when nobody is asking for anything.", Persona, At: At(30, 21, 40)), ct);

        var since = At(27, 9, 0);
        await Write(new Keep<Connection>(new Connection
        {
            Id = Persona, State = ConnectionState.Publishing, Since = since, Failures = 2,
            Trouble = Syndication.Unreachable, RetryAt = new DateTimeOffset(2026, 10, 1, 14, 5, 0, TimeSpan.Zero)
        }), ct);
        await Write(new Keep<Receipt>(Written(gold, At(29, 8, 12), updated: true)), ct);
        await Write(new Keep<Receipt>(Written(leaf, At(28, 10, 6), updated: false)), ct);
        await Write(new Keep<Elsewhere>(new Elsewhere
        {
            Id = gold.Id, AuthorId = Persona, Likes = 128, Reposts = 17, Quotes = 3, Replies = 9,
            References =
            [
                Heard(Reference.Reply, "did:plc:capture4mira", "mira.example", "Mira Okafor",
                    "I've been putting off mending my grandmother's teapot for a year. This is the push I needed.", At(29, 18, 4)),
                Heard(Reference.Quote, "did:plc:capture4tomas", "tomas.example", "Tomás",
                    "The best thing I read this week: a crack as the most honest part of an object.", At(29, 12, 41)),
                Heard(Reference.Reply, "did:plc:capture4june", "june.example", null,
                    "Do you use urushi or the epoxy kits? Curious what holds up for something that sees daily use.", At(29, 9, 15))
            ]
        }), ct);
        await Write(new Keep<Receipt>(new Receipt
        {
            Id = Receipt.Key(Persona, bench.Id), ConnectionId = Persona, PostId = bench.Id, TangentId = "articles", At = At(30, 21, 41)
        }), ct);
        return NoContent();
    }

    private static Reference Heard(string kind, string did, string handle, string? name, string text, DateTimeOffset at)
        => new(kind, did, handle, name, text, at, $"https://bsky.app/profile/{did}/post/3capture{handle[..4]}");

    private static Receipt Written(Post post, DateTimeOffset at, bool updated) => new()
    {
        Id = Receipt.Key(Persona, post.Id), ConnectionId = Persona, PostId = post.Id, TangentId = post.TangentId,
        Version = post.Version, Updated = updated, At = at,
        Document = $"at://{Persona}/{AtprotoAdapter.Documents}/{post.Id}", DocumentCid = "bafyreidocument" + post.Id,
        Note = $"at://{Persona}/{AtprotoAdapter.Notes}/{post.Id}", NoteCid = "bafyreinote" + post.Id
    };

    private static async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await command.Send(ct) switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The Connections seed's {command.GetType().Name} answered {other}.")
    };

    /// <summary>A time in September 2026, in UTC.</summary>
    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 9, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>The TID a client would mint at this time with this clock id.</summary>
    private static string Tid(DateTimeOffset at, int clock)
    {
        var value = ((at - DateTimeOffset.UnixEpoch).Ticks / 10 << 10) | (uint)clock;
        var characters = new char[Posts.Tid.Length];
        for (var position = Posts.Tid.Length - 1; position >= 0; position--)
        {
            characters[position] = Posts.Tid.Alphabet[(int)(value & 31)];
            value >>= 5;
        }

        return new string(characters);
    }

    private sealed record Keep<TEntity>(TEntity Row) : ICommand<TEntity> where TEntity : Entity<TEntity>
    {
        public async Task<Outcome<TEntity>> Run(CancellationToken ct)
        {
            await Row.Save(ct);
            return new Outcome<TEntity>.Done(Row);
        }
    }
}
