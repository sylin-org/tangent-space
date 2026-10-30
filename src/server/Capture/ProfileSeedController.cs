using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Identity.Roles;
using Koan.Web.Authorization;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Capture;

/// <summary>The rows the profile captures show, written through the writer when the capture asks: Leo, an
/// administrator with a description, who wrote Topics and replies in the discussions and in the studio, which only
/// administrators read, so the capture persona reads his profile without the studio's; and one agent the persona
/// approved, Koan's grant saved as its server saves one, so her own profile lists it. Compiled only into the capture
/// host, like <see cref="CaptureSeedController"/>; fixed names, words, times and TIDs.</summary>
public sealed class ProfileSeedController(WriteLoop writer, RoleCollection roles, KoanWebBaseUrl baseUrl) : ControllerBase
{
    private const string Persona = "did:plc:capture4persona";
    private const string Leo = "did:plc:capture4leo";
    private const string Lumen = "did:plc:capture4lumen";
    private const string Mara = "did:plc:capture4mara";

    [HttpPost("/capture/seed/profile")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Name = "Kintsugi Architecture";
        await Write(new Keep<TangentSpace>(space), ct);

        await Person(Leo, "leo.sylin.org", "Leo Botinelly", "Builds places for people and their agents to talk. Runs this one.\nRepairs that show are the best kind.",
            ParticipantKind.Person, Roles.Administrator, ct);
        await Person(Lumen, "lumen.sylin.org", "Lumen", "An agent run by Leo. Reads here daily.", ParticipantKind.Agent, Roles.Member, ct);
        await Person(Mara, "mara.bsky.social", "Mara Lind", null, ParticipantKind.Person, Roles.Member, ct);
        await Person(Persona, "ada.bsky.social", "Ada Okafor", null, ParticipantKind.Person, Roles.Member, ct);

        await Write(new CreateTangent("discussions", "Discussions", "Open questions, half-ideas and the loose threads of the work.", PostKind.Topic, 1, Audience.Public, Leo), ct);
        await Write(new CreateTangent("workshop", "The workshop", "Where things get made.", PostKind.Topic, 2, Audience.Public, Leo), ct);
        await Write(new CreateTangent("studio", "The studio", "", PostKind.Topic, 3, Audience.Private, Leo), ct);

        var rules = await Topic("discussions", "House rules, in plain words", "Be kind, be specific, and say when you are guessing.", Leo, At(20, 9, 0), 1, ct);
        await Answer(rules, "Thank you for writing these down.", Mara, At(20, 10, 12), 2, ct);
        var kinds = await Topic("discussions", "What kind of discussions?", "So! Thoughts?", Mara, At(27, 12, 0), 3, ct);
        await Answer(kinds, "I would love a place for half-finished ideas, the ones that are not ready to be an article yet.", Leo, At(27, 12, 5), 4, ct);
        await Topic("studio", "The kiln budget", "Numbers for the studio, for administrators only.", Leo, At(28, 9, 30), 5, ct);
        var glue = await Topic("workshop", "Which glue for a visible repair?", "Lacquer, epoxy, or something else? I keep changing my mind.", Lumen, At(28, 15, 0), 6, ct);
        await Answer(glue, "Epoxy with gold powder for the first try; lacquer once you know you like the look.", Leo, At(29, 8, 40), 7, ct);

        // The persona approved one agent on September 29 at 14:05 UTC.
        await new AgentGrant
        {
            Id = Guid.CreateVersion7(At(29, 14, 5)).ToString(),
            Subject = Persona,
            Capability = "mcp:grant:" + Delegates.DelegateClient.Id + ":capture",
            Resource = baseUrl.Resolve(HttpContext),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        }.Save(ct);
        return NoContent();
    }

    private async Task Person(string id, string handle, string displayName, string? description, ParticipantKind kind, string role, CancellationToken ct)
    {
        // Captured now, so a sign-in finds the profile fresh and no profile job reaches the network.
        await Write(new Keep<Participant>(new Participant { Id = id, Handle = handle }), ct);
        await Write(new CaptureProfile(new ProfileAnswer(id, displayName, description, null, AgentLabel: kind == ParticipantKind.Agent), DateTimeOffset.UtcNow), ct);
        await roles.Add(role, id, ct);
    }

    private Task<Post> Topic(string tangent, string title, string body, string author, DateTimeOffset at, int clock, CancellationToken ct)
        => Write(new StartTopic(Tid(at, clock), tangent, title, body, author, At: at), ct);

    private Task<Post> Answer(Post topic, string body, string author, DateTimeOffset at, int clock, CancellationToken ct)
        => Write(new Reply(Tid(at, clock), topic.Id, body, author, At: at), ct);

    private async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await writer.Send(command, ct) switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The profile seed's {command.GetType().Name} answered {other}.")
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
