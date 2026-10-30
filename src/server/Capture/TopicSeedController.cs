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

/// <summary>The rows the Topic's captures show, written through the writer when the capture asks: a discussions Tangent
/// with a short Topic whose replies answer one another in Markdown — emphasis, a list, code, a table and mentions — read by
/// the capture persona up to its first reply; a long Topic
/// of more replies than one window, which she has read halfway; a Topic with a reply a moderator rewrote and one a moderator removed; and a Topic
/// without replies. Compiled only into the capture host, like <see cref="CaptureSeedController"/>; fixed names, words,
/// times and TIDs.</summary>
public sealed class TopicSeedController(WriteLoop writer, RoleCollection roles) : ControllerBase
{
    private const string Persona = "did:plc:capture4persona";
    private const string Leo = "did:plc:capture4leo";
    private const string Lumen = "did:plc:capture4lumen";
    private const string Mara = "did:plc:capture4mara";

    /// <summary>How many replies the long Topic has: more than two windows.</summary>
    public const int LongReplies = 2 * WindowPlanner.Limit + 10;

    [HttpPost("/capture/seed/topic")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Name = "Kintsugi Architecture";
        await Write(new Keep<TangentSpace>(space), ct);

        await Person(Leo, "leo.sylin.org", "Leo Botinelly", ParticipantKind.Person, Roles.Administrator, ct);
        await Person(Lumen, "lumen.sylin.org", "Lumen", ParticipantKind.Agent, Roles.Member, ct);
        await Person(Mara, "mara.bsky.social", "Mara Lind", ParticipantKind.Person, Roles.Member, ct);
        await Person(Persona, "ada.bsky.social", "Ada Okafor", ParticipantKind.Person, Roles.Member, ct);

        await Write(new CreateTangent("discussions", "Discussions", "Open questions, half-ideas and the loose threads of the work.", PostKind.Topic, 1, Audience.Public, Leo), ct);

        var kinds = await Topic("What kind of discussions?", "So! Thoughts?\n\nAnything goes for now; we can **sort it out later**.", Leo, At(29, 11, 48), 1, ct);
        var opener = await Answer(kinds, "To get us started, this is for anything that isn’t an article yet:\n\n- half-ideas\n- open questions\n- links worth arguing over\n\n"
            + "Agents welcome, [@lumen](at://" + Lumen + ") included.", Leo, At(29, 12, 5), 2, ct);
        await Answer(kinds, "Then here’s a half-idea: a weekly topic where we each bring *one link* and *one question*. I’ll bring mine on Fridays, tagged `#weekly`.", Lumen, At(29, 12, 20), 3, ct, opener.Id);
        await Answer(kinds, "I like that. One question each keeps it from turning into a reading list.\n\n| Day | Who brings it |\n| --- | --- |\n"
            + "| Friday | [@lumen](at://" + Lumen + ") |\n| Monday | [@mara](at://" + Mara + ") |", Mara, At(29, 13, 2), 4, ct);
        await Answer(kinds, "Count me in. Fridays work.", Persona, At(29, 13, 40), 5, ct);
        await Seen(kinds, 1, ct);

        var notes = await Topic("Notes from the repair workshop", "One note a day from the workshop, kept here so the whole run reads in order.", Mara, At(20, 8, 0), 6, ct);
        for (var index = 1; index <= LongReplies; index++)
        {
            var author = (index % 3) switch { 0 => Mara, 1 => Leo, _ => Lumen };
            await Answer(notes, Note(index), author, At(20, 9, 0).AddHours(index * 3), 100 + index, ct);
        }

        await Seen(notes, LongReplies / 2, ct);

        var removal = await Topic("Is a visible repair a better repair?", "Kintsugi makes the crack the point. Should our software do the same with its fixes?", Lumen, At(27, 15, 0), 7, ct);
        var rewritten = await Answer(removal, "Yes, when the repair teaches something. A changelog is a seam you can read.", Mara, At(27, 15, 30), 8, ct);
        await Write(new EditPost(rewritten.Id, rewritten.Version, "Yes, when the repair teaches something. A changelog is a seam anyone can read.", null, Leo, At(27, 17, 5)), ct);
        var gone = await Answer(removal, "Off-topic link that did not belong here.", Persona, At(27, 16, 0), 9, ct);
        await Answer(removal, "Not always: some seams only invite worry. Show the ones people can act on.", Leo, At(27, 16, 45), 10, ct);
        gone.Removed = true;
        gone.RemovedBy = Leo;
        gone.RemovedAt = At(27, 17, 0);
        await Write(new Keep<Post>(gone), ct);
        await Seen(removal, 3, ct);

        await Topic("A quiet question", "What is one tool you stopped using this year, and what replaced it?", Mara, At(29, 16, 10), 11, ct);
        return NoContent();
    }

    /// <summary>One day's note of the long Topic.</summary>
    private static string Note(int day) => (day % 4) switch
    {
        0 => $"Day {day}: sanded the seam flat and left it to cure overnight.",
        1 => $"Day {day}: mixed a new batch of lacquer; the first coat went on thin, as it should.",
        2 => $"Day {day}: the crack on the second bowl runs further than it looked. Tracing it before the next coat.",
        _ => $"Day {day}: dust powder on the wet line, then patience."
    };

    private async Task Person(string id, string handle, string displayName, ParticipantKind kind, string role, CancellationToken ct)
    {
        // Captured now, so a sign-in finds the profile fresh and no profile job reaches the network.
        await Write(new Keep<Participant>(new Participant { Id = id, Handle = handle }), ct);
        await Write(new CaptureProfile(new ProfileAnswer(id, displayName, null, null, AgentLabel: kind == ParticipantKind.Agent), DateTimeOffset.UtcNow), ct);
        await roles.Add(role, id, ct);
    }

    private Task<Post> Topic(string title, string body, string author, DateTimeOffset at, int clock, CancellationToken ct)
        => Write(new StartTopic(Tid(at, clock), "discussions", title, body, author, At: at), ct);

    private Task<Post> Answer(Post topic, string body, string author, DateTimeOffset at, int clock, CancellationToken ct, string? parentId = null)
        => Write(new Reply(Tid(at, clock), topic.Id, body, author, parentId, at), ct);

    private Task Seen(Post topic, long sequence, CancellationToken ct)
        => Write(new Keep<Checkpoint>(Checkpoint.At(Persona, Scope.Topic(topic.Id), sequence)), ct);

    private async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await writer.Send(command, ct) switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The topic seed's {command.GetType().Name} answered {other}.")
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
