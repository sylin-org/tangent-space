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

/// <summary>The rows the Topic capture's report states show, written through the writer when the capture asks: a
/// discussions Tangent with a Topic Leo started and Mara answered, and the capture persona's report of Mara's reply.
/// Compiled only into the capture host, like <see cref="CaptureSeedController"/>; fixed names, words, times and TIDs, so
/// the reply is at /t/discussions/glazes-we-keep-coming-back-to/3mw6hf56bs224 and the persona's report is
/// 3mw6iivles225.</summary>
public sealed class ReportedSeedController(WriteLoop writer, RoleCollection roles) : ControllerBase
{
    private const string Persona = "did:plc:capture4persona";
    private const string Leo = "did:plc:capture4leo";
    private const string Mara = "did:plc:capture4mara";

    [HttpPost("/capture/seed/topic-reported")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Name = "Kintsugi Architecture";
        await Write(new Keep<TangentSpace>(space), ct);

        await Person(Leo, "leo.sylin.org", "Leo Botinelly", Roles.Administrator, ct);
        await Person(Mara, "mara.bsky.social", "Mara Lind", Roles.Member, ct);
        await Person(Persona, "ada.bsky.social", "Ada Okafor", Roles.Member, ct);

        await Write(new CreateTangent("discussions", "Discussions", "Open questions, half-ideas and the loose threads of the work.", PostKind.Topic, 1, Audience.Public, Leo), ct);

        var glazes = await Write(new StartTopic(Tid(At(23, 9, 0), 1), "discussions", "Glazes we keep coming back to",
            "Which glaze would you keep if the studio could only keep one?", Leo, At: At(23, 9, 0)), ct);
        var answer = await Write(new Reply(Tid(At(23, 9, 45), 2), glazes.Id, "Celadon. Anyone who says otherwise has not fired enough of it.", Mara,
            At: At(23, 9, 45)), ct);
        await Write(new ReportPost(Tid(At(23, 10, 5), 3), answer.Id, PostBody.Of("This reads as a dig at the people who prefer tenmoku."), Persona,
            At(23, 10, 5)), ct);
        return NoContent();
    }

    private async Task Person(string id, string handle, string displayName, string role, CancellationToken ct)
    {
        // Captured now, so a sign-in finds the profile fresh and no profile job reaches the network.
        await Write(new Keep<Participant>(new Participant { Id = id, Handle = handle }), ct);
        await Write(new CaptureProfile(new ProfileAnswer(id, displayName, null, null, AgentLabel: false), DateTimeOffset.UtcNow), ct);
        await roles.Add(role, id, ct);
    }

    private async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await writer.Send(command, ct) switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The reported seed's {command.GetType().Name} answered {other}.")
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
