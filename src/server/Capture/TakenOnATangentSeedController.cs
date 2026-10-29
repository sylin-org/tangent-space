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

/// <summary>The rows take this on a tangent's captures show, written through the writer when the capture asks: a
/// discussions Tangent where Leo's reply in "Where should the glue notes live?" was taken on a tangent into the workshop
/// as "Adhesives we trust"; and an off-the-record Tangent only administrators read, whose Post was taken into the
/// workshop as "The kiln budget", so the capture persona, a member, reads that it came from a Post she cannot see.
/// Compiled only into the capture host, like <see cref="CaptureSeedController"/>; fixed names, words, times and TIDs, so
/// the origin is at /t/discussions/where-should-the-glue-notes-live/3mwfyixhb2224.</summary>
public sealed class TakenOnATangentSeedController(WriteLoop writer, RoleCollection roles) : ControllerBase
{
    private const string Persona = "did:plc:capture4persona";
    private const string Leo = "did:plc:capture4leo";
    private const string Mara = "did:plc:capture4mara";

    [HttpPost("/capture/seed/tangent-taken")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Name = "Kintsugi Architecture";
        await Write(new Keep<TangentSpace>(space), ct);

        await Person(Leo, "leo.sylin.org", "Leo Botinelly", Roles.Administrator, ct);
        await Person(Mara, "mara.bsky.social", "Mara Lind", Roles.Member, ct);
        await Person(Persona, "ada.bsky.social", "Ada Okafor", Roles.Member, ct);

        await Write(new CreateTangent("discussions", "Discussions", "Open questions, half-ideas and the loose threads of the work.", PostKind.Topic, 1, Audience.Public, Leo), ct);
        await Write(new CreateTangent("workshop", "The workshop", "What happens at the bench, written down.", PostKind.Topic, 2, Audience.Public, Leo), ct);
        await Write(new CreateTangent("off-the-record", "Off the record", "Where the administrators plan.", PostKind.Topic, 3, Audience.Private, Leo), ct);

        var glue = await Write(new StartTopic(Tid(At(26, 9, 0), 1), "discussions", "Where should the glue notes live?",
            "Every repair this month has a note about which glue held. Where should those notes go?", Mara, At: At(26, 9, 0)), ct);
        var reply = await Write(new Reply(Tid(At(26, 9, 40), 2), glue.Id, "They deserve their own topic in the workshop: what we used, on what, and whether it held.",
            Leo, At: At(26, 9, 40)), ct);
        await Write(new Reply(Tid(At(26, 10, 5), 3), glue.Id, "Agreed. A list we can keep adding to.", Persona, At: At(26, 10, 5)), ct);
        await Write(new StartTopic(Tid(At(26, 11, 0), 4), "workshop", "Adhesives we trust",
            "Starting the list Leo asked for. Urushi on ceramics: held after three firings.", Mara, At: At(26, 11, 0), OriginId: reply.Id), ct);

        var plan = await Write(new StartTopic(Tid(At(25, 14, 0), 5), "off-the-record", "Next season’s spending",
            "The kiln needs new elements before winter.", Leo, At: At(25, 14, 0)), ct);
        await Write(new StartTopic(Tid(At(25, 15, 0), 6), "workshop", "The kiln budget",
            "The kiln gets new elements before winter. Tell us what you would fire first.", Leo, At: At(25, 15, 0), OriginId: plan.Id), ct);
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
        var other => throw new InvalidOperationException($"The take-this-on-a-tangent seed's {command.GetType().Name} answered {other}.")
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
