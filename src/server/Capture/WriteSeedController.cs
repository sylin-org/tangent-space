using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Capture;

/// <summary>The rows the new-topic and edit captures show, written through the writer when the capture asks: a place
/// with a discussions Tangent, a Topic the capture persona started, and replies to it by people who are not her, so
/// her own Post opens the edit form and theirs say why she may not change them. Compiled only into the capture host,
/// like <see cref="CaptureSeedController"/>; fixed names, words, times and TIDs, so the edit screen's paths in
/// <c>src/client/capture/screens.ts</c> name these Posts.</summary>
public sealed class WriteSeedController(WriteLoop writer, RoleCollection roles) : ControllerBase
{
    private const string Persona = "did:plc:capture4persona";
    private const string Leo = "did:plc:capture4leo";
    private const string Mara = "did:plc:capture4mara";

    [HttpPost("/capture/seed/write")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Name = "Kintsugi Architecture";
        await Write(new Keep<TangentSpace>(space), ct);

        await Person(Leo, "leo.sylin.org", "Leo Botinelly", ParticipantKind.Person, Roles.Administrator, ct);
        await Person(Mara, "mara.bsky.social", "Mara Lind", ParticipantKind.Person, Roles.Member, ct);
        await Person(Persona, "ada.bsky.social", "Ada Okafor", ParticipantKind.Person, Roles.Member, ct);

        await Write(new CreateTangent("discussions", "Discussions", "Open questions, half-ideas and the loose threads of the work.", PostKind.Topic, 1, Audience.Public, Leo), ct);

        // Her Topic is 3mwgqaw5is227 at /t/discussions/what-should-an-introduction-say; Leo's reply is 3mwnstt4b222d.
        var intro = await Write(new StartTopic(Tid(At(26, 16, 45), 5), "discussions", "What should an introduction say?",
            "Who runs it, what it is good at, and when it will not answer. What else belongs there?", Persona, At: At(26, 16, 45)), ct);
        await Write(new Reply(Tid(At(29, 12, 20), 9), intro.Id, "A line on what it will refuse to do. That is the part people look for.", Leo, At: At(29, 12, 20)), ct);
        await Write(new Reply(Tid(At(29, 13, 5), 10), intro.Id, "And who to ask when it gets something wrong.", Mara, At: At(29, 13, 5)), ct);
        return NoContent();
    }

    private async Task Person(string id, string handle, string displayName, ParticipantKind kind, string role, CancellationToken ct)
    {
        // Captured now, so a sign-in finds the profile fresh and no profile job reaches the network.
        await Write(new Keep<Participant>(new Participant { Id = id, Handle = handle }), ct);
        await Write(new CaptureProfile(new ProfileAnswer(id, displayName, null, null, AgentLabel: kind == ParticipantKind.Agent), DateTimeOffset.UtcNow), ct);
        await roles.Add(role, id, ct);
    }

    private async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await writer.Send(command, ct) switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The write seed's {command.GetType().Name} answered {other}.")
    };

    /// <summary>A time in September 2026, in UTC.</summary>
    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 9, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>The TID a client would mint at this time with this clock id: microseconds since the epoch in the high
    /// bits, the clock id in the low ten.</summary>
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
