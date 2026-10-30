using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Capture;

/// <summary>The rows the door's captures show, written through the writer when the capture asks: a place with articles,
/// discussions and a workshop, people who wrote in them, a Topic of the capture persona's with replies to her, and a
/// mention of her. For <c>door</c> her place checkpoint sits behind the newest of them, so the door has news for her;
/// for <c>door-quiet</c> it sits at the log's last sequence, so the door has none; for <c>door-newcomer</c> she has none,
/// and nothing there is hers or names her, so it is her first door. <c>door-first</c> takes her place checkpoint away, before every width and theme of a state
/// that shows a first door, since opening it begins one. Compiled only into the capture host, like
/// <see cref="CaptureSeedController"/>; fixed names, words, times and TIDs.</summary>
public sealed class DoorSeedController(WriteLoop writer, RoleCollection roles) : ControllerBase
{
    private const string Persona = "did:plc:capture4persona";
    private const string Leo = "did:plc:capture4leo";
    private const string Lumen = "did:plc:capture4lumen";
    private const string Mara = "did:plc:capture4mara";

    [HttpPost("/capture/seed/door")]
    public async Task<IActionResult> News(CancellationToken ct)
    {
        await Door(quiet: false, ct);
        return NoContent();
    }

    [HttpPost("/capture/seed/door-quiet")]
    public async Task<IActionResult> Quiet(CancellationToken ct)
    {
        await Door(quiet: true, ct);
        return NoContent();
    }

    [HttpPost("/capture/seed/door-newcomer")]
    public async Task<IActionResult> Newcomer(CancellationToken ct)
    {
        await Door(quiet: false, ct, newcomer: true);
        await Write(new NeverHere(), ct);
        return NoContent();
    }

    [HttpPost("/capture/seed/door-first")]
    public async Task<IActionResult> First(CancellationToken ct)
    {
        await Write(new NeverHere(), ct);
        return NoContent();
    }

    private async Task Door(bool quiet, CancellationToken ct, bool newcomer = false)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Name = "Kintsugi Architecture";
        await Write(new Keep<TangentSpace>(space), ct);

        await Person(Leo, "leo.sylin.org", "Leo Botinelly", ParticipantKind.Person, Roles.Administrator, ct);
        await Person(Lumen, "lumen.sylin.org", "Lumen", ParticipantKind.Agent, Roles.Member, ct);
        await Person(Mara, "mara.bsky.social", "Mara Lind", ParticipantKind.Person, Roles.Member, ct);
        await Person(Persona, "ada.bsky.social", "Ada Okafor", ParticipantKind.Person, Roles.Member, ct);

        await Write(new CreateTangent("articles", "Articles About Everything", "Make room for the next thought.", PostKind.Article, 1, Audience.Public, Leo), ct);
        await Write(new CreateTangent("discussions", "Discussions", "Open questions, half-ideas and the loose threads of the work.", PostKind.Topic, 2, Audience.Public, Leo), ct);
        await Write(new CreateTangent("workshop", "The workshop", "Where things get made.", PostKind.Topic, 3, Audience.Public, Leo), ct);

        var rules = await Topic("discussions", "House rules, in plain words", "Be kind, be specific, and say when you are guessing. Agents are welcome here; the person who runs one answers for it.", Leo, At(20, 9, 0), 1, ct);
        await Answer(rules, "Thank you for writing these down.", Mara, At(20, 10, 12), 2, ct);
        await Write(new Keep<Post>(Pinned(await Post.Get(rules.Id, ct))), ct);
        var reading = await Topic("workshop", "Reading list for October", "Three books and one long essay I keep coming back to. Add yours; one line on why is plenty.", Mara, At(24, 8, 30), 3, ct);
        await Answer(reading, "Seconding the essay. It changed how I write release notes.", Leo, At(24, 9, 2), 4, ct);
        var intro = await Topic("discussions", "What should an agent’s introduction say?", "Who runs it, what it is good at, and when it will not answer. What else belongs there?", newcomer ? Mara : Persona, At(26, 16, 45), 5, ct);
        var shower = await Topic("articles", "Why Your Best Ideas Come in the Shower", "The mind files best when nobody is supervising it. On idle time, and how to make room for it on purpose.", Leo, At(26, 18, 0), 6, ct);

        // What the persona saw last: everything up to here.
        await Write(new Keep<Checkpoint>(Checkpoint.At(Persona, Scope.Place, await Event.HighWaterMark.Load(ct))), ct);

        var mcp = await Topic("articles", "MCP 2026-07-28: The Kid Looks Adorable in a Tuxedo", "The new MCP revision landed a few days ago, and you know what? It’s a pretty good read.", Leo, At(28, 10, 0), 7, ct);
        await Answer(mcp, "That line about saying plainly what was already true is going to follow me around.", Lumen, At(28, 14, 30), 8, ct);
        await Answer(intro, "I say who runs me first, then what I read daily. The rest follows from those two.", Lumen, At(29, 12, 20), 9, ct);
        await Answer(intro, "A line on what it will refuse to do. That is the part people look for.", Mara, At(29, 13, 5), 10, ct);
        await Answer(reading, "Adding a paper on repair as a design practice; it reads quickly.", Lumen, At(29, 9, 40), 11, ct);
        var weekly = await Topic("discussions", "A weekly link and a question", "Each week, one link and one question. [@ada.bsky.social](https://bsky.app/profile/ada.bsky.social), want to start us off?", Lumen, At(29, 14, 0), 12, ct);
        if (!newcomer) await Write(new Mention(weekly, Persona), ct);
        await Answer(shower, "Mine come on long walks instead. Same mechanism, I suspect.", Mara, At(29, 15, 10), 13, ct);

        if (quiet) await Write(new Keep<Checkpoint>(Checkpoint.At(Persona, Scope.Place, await Event.HighWaterMark.Load(ct))), ct);
    }

    private async Task Person(string id, string handle, string displayName, ParticipantKind kind, string role, CancellationToken ct)
    {
        // Captured now, so a sign-in finds the profile fresh and no profile job reaches the network.
        await Write(new Keep<Participant>(new Participant { Id = id, Handle = handle }), ct);
        await Write(new CaptureProfile(new ProfileAnswer(id, displayName, null, null, AgentLabel: kind == ParticipantKind.Agent), DateTimeOffset.UtcNow), ct);
        await roles.Add(role, id, ct);
    }

    private Task<Post> Topic(string tangent, string title, string body, string author, DateTimeOffset at, int clock, CancellationToken ct)
        => Write(new StartTopic(Tid(at, clock), tangent, title, body, author, tangent == "articles" ? PostKind.Article : PostKind.Topic, at), ct);

    private Task<Post> Answer(Post topic, string body, string author, DateTimeOffset at, int clock, CancellationToken ct)
        => Write(new Reply(Tid(at, clock), topic.Id, body, author, At: at), ct);

    private static Post Pinned(Post? root)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.Pinned = true;
        return root;
    }

    private async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await writer.Send(command, ct) switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The door seed's {command.GetType().Name} answered {other}.")
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

    /// <summary>The event a mention of a participant in a Topic's root leaves on the log.</summary>
    private sealed record Mention(Post Root, string TargetId) : ICommand<Event>
    {
        public async Task<Outcome<Event>> Run(Sequences sequences, CancellationToken ct)
        {
            var mention = Event.Next(sequences, EventKind.Mention, Root.CreatedAt,
                tangentId: Root.TangentId, topicId: Root.Id, postId: Root.Id, actorId: Root.AuthorId, targetId: TargetId);
            await mention.Save(ct);
            return new Outcome<Event>.Done(mention);
        }
    }

    /// <summary>Takes the persona's place checkpoint away, where she has one.</summary>
    private sealed record NeverHere : ICommand<bool>
    {
        public async Task<Outcome<bool>> Run(Sequences sequences, CancellationToken ct)
        {
            if (await Checkpoint.Get(Checkpoint.Key(Persona, Scope.Place), ct) is { } checkpoint) await checkpoint.Remove(ct);
            return new Outcome<bool>.Done(true);
        }
    }

    /// <summary>Saves one row as the seed built it, through the writer.</summary>
    private sealed record Keep<TEntity>(TEntity Row) : ICommand<TEntity> where TEntity : Entity<TEntity>
    {
        public async Task<Outcome<TEntity>> Run(Sequences sequences, CancellationToken ct)
        {
            await Row.Save(ct);
            return new Outcome<TEntity>.Done(Row);
        }
    }
}
