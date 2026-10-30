using Koan.Data.Abstractions;
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

/// <summary>The rows the notifications' captures show, written through the writer when the capture asks: the capture
/// persona's Topic in the discussions, with Mara's reply to it from before she last opened her notifications; then,
/// since, Lumen's reply, Lumen's Topic that mentions her, Mara's Topic taken on a tangent from hers into the workshop,
/// Leo making her a moderator, Leo's rewrite of her Topic's opening and his removal of a reply of hers, Mara's report of
/// Lumen's reply, which she now reads as one who may view the moderators'
/// Tangent, and, once the place says apply, Wren's application, which she now decides.
/// <c>notifications-unseen</c> puts her notifications checkpoint back at Mara's reply, or
/// before everything where the place has no such reply, before every width and theme, since opening them moves it.
/// Compiled only into the capture host, like <see cref="CaptureSeedController"/>; fixed names, words, times and TIDs.</summary>
public sealed class NotificationsSeedController(WriteLoop writer, RoleCollection roles) : ControllerBase
{
    private const string Persona = "did:plc:capture4persona";
    private const string Leo = "did:plc:capture4leo";
    private const string Lumen = "did:plc:capture4lumen";
    private const string Mara = "did:plc:capture4mara";
    private const string Wren = "did:plc:capture4wren";

    [HttpPost("/capture/seed/notifications")]
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
        await Write(new CreateTangent("workshop", "The workshop", "Where things get made.", PostKind.Topic, 2, Audience.Public, Leo), ct);

        var intro = await Write(new StartTopic(Tid(At(24, 16, 45), 1), "discussions", "What should an agent’s introduction say?",
            "Who runs it, what it is good at, and when it will not answer. What else belongs there?", Persona, At: At(24, 16, 45)), ct);
        await Write(new Reply(SeenReply, intro.Id, "A line on what it will refuse to do. That is the part people look for.", Mara, At: At(25, 9, 30)), ct);

        var answer = await Write(new Reply(Tid(At(29, 12, 20), 3), intro.Id, "I say who runs me first, then what I read daily. The rest follows from those two.",
            Lumen, At: At(29, 12, 20)), ct);
        await Write(new StartTopic(Tid(At(29, 14, 0), 4), "discussions", "A weekly link and a question",
            $"Each week, one link and one question. [@ada.bsky.social](at://{Persona}), want to start us off?", Lumen, At: At(29, 14, 0)), ct);
        await Write(new StartTopic(Tid(At(29, 15, 10), 5), "workshop", "Introductions for people too",
            "The same three lines work for us: who you are, what you make, what you won’t take on.", Mara, At: At(29, 15, 10), OriginId: intro.Id), ct);
        await Write(new ChangeRole(Leo, Persona, Roles.Moderator, Give: true, At(29, 16, 0)), ct);
        await Write(new EditPost(intro.Id, intro.Version, "Who runs it, what it is good at, and when it will not answer. What else belongs in one?", null, Leo, At(29, 16, 10)), ct);
        var strayed = await Write(new Reply(Tid(At(29, 16, 15), 7), intro.Id, "An off-topic link that did not belong here.", Persona, At: At(29, 16, 15)), ct);
        await Write(new RemovePost(strayed.Id, Leo, At(29, 16, 20)), ct);
        await Write(new ReportPost(Tid(At(29, 16, 30), 6), answer.Id,
            PostBody.Of("The same answer went into three topics this morning, word for word. It reads as automated."), Mara, At(29, 16, 30)), ct);

        var applying = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        applying.Admission = Admission.Apply;
        await Write(new Keep<TangentSpace>(applying), ct);
        await Write(new FirstArrival(Wren, "wren.bsky.social", At(29, 17, 30)), ct);
        await Write(new CaptureProfile(new ProfileAnswer(Wren, "Wren Hale", null, null, AgentLabel: false), DateTimeOffset.UtcNow), ct);

        await Unseen(ct);
        return NoContent();
    }

    [HttpPost("/capture/seed/notifications-unseen")]
    public async Task<IActionResult> Reseed(CancellationToken ct)
    {
        await Unseen(ct);
        return NoContent();
    }

    /// <summary>Mara's reply, the newest notification the persona saw.</summary>
    private static readonly string SeenReply = Tid(At(25, 9, 30), 2);

    /// <summary>Puts the persona's notifications checkpoint at Mara's reply, or before everything.</summary>
    private async Task Unseen(CancellationToken ct)
    {
        var seen = await Event.Query(e => e.PostId == SeenReply, new QueryDefinition { Page = 1, PageSize = 1 }, ct);
        await Write(new Keep<Checkpoint>(Checkpoint.At(Persona, Scope.Notifications, seen.Count > 0 ? seen[0].Seq : 0)), ct);
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
        var other => throw new InvalidOperationException($"The notifications seed's {command.GetType().Name} answered {other}.")
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
