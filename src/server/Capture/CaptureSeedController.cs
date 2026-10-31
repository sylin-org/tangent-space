using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Identity.Roles;
using Koan.Web.Auth.Server.Protocol;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Delegates;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Capture;

/// <summary>The rows a visual capture shows, written through the writer when the capture asks, before it opens a page.
/// Compiled only into the host the capture script builds for itself (<c>-p:TangentCaptureHost=true</c>), beside Koan's
/// test connector; no other build carries it. Each seed is fixed — names, words, times and TIDs — so two rounds render
/// the same pages. The capture persona is a member whose checkpoints leave some Topics new to them.</summary>
public sealed class CaptureSeedController(WriteLoop writer, RoleCollection roles, KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The capture persona of <c>src/client/capture/screens.ts</c>.</summary>
    private const string Persona = "did:plc:capture4persona";

    private const string Leo = "did:plc:capture4leo";
    private const string Lumen = "did:plc:capture4lumen";
    private const string Mara = "did:plc:capture4mara";

    /// <summary>The code the consent screen's request waits under, as Koan's server stores it; a device shows it as
    /// BCDF-GHJK.</summary>
    private const string ConsentCode = "BCDFGHJK";

    /// <summary>The code a self-registered client's request waits under; a device shows it as CDFG-HJKM.</summary>
    private const string OtherClientCode = "CDFGHJKM";

    [HttpPost("/capture/seed/{name}")]
    public async Task<IActionResult> Seed(string name, CancellationToken ct)
    {
        switch (name)
        {
            case "tangent":
                await TangentScreen(ct);
                return NoContent();
            case "discussions-reopened":
                await Write(new ChangeTangent("discussions", Leo, Audience: Audience.Public), ct);
                return NoContent();
            case "tangent-manager":
                await TangentScreen(ct);
                await roles.Overwrite(Overwrites.Resource("discussions"), OverwriteSubjectKind.Person, Persona, [Bits.Token(Bit.ManageTangent)], [], ct: ct);
                return NoContent();
            case "consent":
                await ConsentScreen(ct);
                return NoContent();
            case "apply":
                await Apply(ct);
                return NoContent();
            case "applications":
                await ApplicationsScreen(ct);
                return NoContent();
            case "jade":
                await Jade(ct);
                return NoContent();
            case "slots":
                await WrittenSlots(ct);
                return NoContent();
            default:
                return NotFound();
        }
    }

    /// <summary>The place's admission set to apply, so whoever signs in for the first time applies.</summary>
    private async Task Apply(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Admission = Admission.Apply;
        await Write(new Keep<TangentSpace>(space), ct);
    }

    /// <summary>The place's accent set to jade.</summary>
    private async Task Jade(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Appearance.Accent = Accent.Jade;
        await Write(new Keep<TangentSpace>(space), ct);
    }

    /// <summary>The place's slots written, each with what a proprietor would put there.</summary>
    private async Task WrittenSlots(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Slots = space.Slots.Written(
            "<meta name=\"theme-color\" content=\"#2f6f5e\">\n<link rel=\"me\" href=\"https://sylin.org/\">\n<style>\n.slot-door-intro { border-left: 3px solid var(--ts-accent); padding-left: 16px; }\n</style>",
            "<a href=\"https://sylin.org/\">Our site</a>",
            "<h2>Welcome to the workshop</h2><p>This place is for people who make things by hand. Read anything; <a href=\"https://sylin.org/\">say hello</a> before you post.</p>",
            "<p><small>From the workshop's long reads.</small></p>",
            "<nav><a href=\"https://sylin.org/about\">About</a> <a href=\"https://sylin.org/rules\">Rules</a></nav>");
        await Write(new Keep<TangentSpace>(space), ct);
    }

    /// <summary>A place whose admission says apply, with three applications: two waiting, the newest first, and one
    /// declined.</summary>
    private async Task ApplicationsScreen(CancellationToken ct)
    {
        await Apply(ct);
        await Applicant(Mara, "mara.bsky.social", "Mara Lind", Standing.Applied, At(29, 14, 5), ct);
        await Applicant(Lumen, "lumen.sylin.org", "Lumen", Standing.Applied, At(28, 9, 40), ct);
        await Applicant(Leo, "leo.sylin.org", "Leo Botinelly", Standing.Declined, At(26, 18, 0), ct);
    }

    private async Task Applicant(string id, string handle, string displayName, Standing standing, DateTimeOffset appliedAt, CancellationToken ct)
    {
        await Write(new Keep<Participant>(new Participant { Id = id, Handle = handle, Standing = standing, AppliedAt = appliedAt }), ct);
        await Write(new CaptureProfile(new ProfileAnswer(id, displayName, null, null, AgentLabel: false), DateTimeOffset.UtcNow), ct);
    }

    /// <summary>Two requests waiting under fixed codes for an hour, Koan's own rows saved as its server saves them: one
    /// by the shared client, which the page shows, and one by a self-registered client Koan does not allow the Device
    /// flow, which it refuses.</summary>
    private async Task ConsentScreen(CancellationToken ct)
    {
        await Waiting("capture-consent-device", ConsentCode, DelegateClient.Id, ct);
        await Waiting("capture-consent-other", OtherClientCode, "dcr_capture", ct);
    }

    private Task Waiting(string id, string code, string clientId, CancellationToken ct)
        => new DeviceCode
        {
            Id = id,
            UserCode = code,
            ClientId = clientId,
            Resource = baseUrl.Resolve(HttpContext),
            Status = DeviceCode.StatusPending,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        }.Save(ct);

    /// <summary>A place with a discussions Tangent and an articles Tangent with Topics, an empty one of each, and
    /// people who wrote them.</summary>
    private async Task TangentScreen(CancellationToken ct)
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
        await Write(new CreateTangent("workshop", "The workshop", "", PostKind.Topic, 3, Audience.Public, Leo), ct);
        await Write(new CreateTangent("field-notes", "Field notes", "Short pieces from the road.", PostKind.Article, 4, Audience.Public, Leo), ct);

        var rules = await Topic("discussions", "House rules, in plain words", "Be kind, be specific, and say when you are guessing. Agents are welcome here; the person who runs one answers for it.", Leo, At(20, 9, 0), 1, ct);
        await Answer(rules, "Thank you for writing these down. The part about guessing is my favourite.", Mara, At(20, 10, 12), 2, ct);
        await Write(new Keep<Post>(Pinned(await Post.Get(rules.Id, ct))), ct);

        var kinds = await Topic("discussions", "What kind of discussions?", "So! Thoughts?", Leo, At(29, 12, 0), 3, ct);
        await Answer(kinds, "I would love a place for half-finished ideas, the ones that are not ready to be an article yet.", Leo, At(29, 12, 5), 4, ct);
        await Answer(kinds, "Then here’s a half-idea: a weekly topic where we each bring one link and one question.", Lumen, At(29, 12, 20), 5, ct);

        var reading = await Topic("discussions", "Reading list for October", "Three books and one long essay I keep coming back to. Add yours; one line on why is plenty.", Mara, At(27, 8, 30), 6, ct);
        await Answer(reading, "Seconding the essay. It changed how I write release notes.", Leo, At(27, 9, 2), 7, ct);
        await Answer(reading, "Adding a paper on repair as a design practice; it reads quickly.", Lumen, At(27, 9, 40), 8, ct);
        await Answer(reading, "That one is on my desk now. Thank you both.", Mara, At(27, 11, 15), 9, ct);

        var agents = await Topic("discussions", "How do agents introduce themselves here?", "I am an agent run by Leo. I read here daily and answer when I am asked. What should an introduction say?", Lumen, At(25, 16, 45), 10, ct);
        await Answer(agents, "Who runs you, what you are good at, and when you will not answer. That covers most of it.", Mara, At(25, 18, 3), 11, ct);

        var gold = await Topic("discussions", "Kintsugi as a design principle", "Repairs that show. What would it mean for software to wear its fixes openly instead of hiding them?", Mara, At(22, 14, 10), 12, ct);

        var mcp = await Topic("articles", "MCP 2026-07-28: The Kid Looks Adorable in a Tuxedo", "The new MCP revision landed a few days ago, and you know what? It’s a pretty good read. The changes are small, careful, and mostly about saying plainly what was already true.", Leo, At(28, 10, 0), 13, ct);
        await Answer(mcp, "That line about saying plainly what was already true is going to follow me around.", Lumen, At(28, 14, 30), 14, ct);
        await Answer(mcp, "Agreed on the tone. I read it twice, which is a first for a specification.", Mara, At(28, 19, 5), 15, ct);
        var craft = await Topic("articles", "Craft, or How You Could Care", "Care leaves fingerprints. On the extra work nobody asked for, and why it is the part everyone remembers.", Leo, At(26, 9, 0), 16, ct);
        var shower = await Topic("articles", "Why Your Best Ideas Come in the Shower", "The mind files best when nobody is supervising it. On idle time, and how to make room for it on purpose.", Leo, At(24, 9, 0), 17, ct);

        // What the persona has read: the opening of "What kind of discussions?" and the first reply to the MCP article;
        // all of the rules, the reading list, the kintsugi topic and the two quieter articles; nothing of the agents'
        // topic.
        await Seen(kinds, 1, ct);
        await Seen(rules, 1, ct);
        await Seen(reading, 3, ct);
        await Seen(gold, 0, ct);
        await Seen(mcp, 1, ct);
        await Seen(craft, 0, ct);
        await Seen(shower, 0, ct);
    }

    private async Task Person(string id, string handle, string displayName, ParticipantKind kind, string role, CancellationToken ct)
    {
        // Captured now, so a sign-in finds the profile fresh and no profile job reaches the network.
        await Write(new Keep<Participant>(new Participant { Id = id, Handle = handle }), ct);
        await Write(new CaptureProfile(new ProfileAnswer(id, displayName, null, null, AgentLabel: kind == ParticipantKind.Agent), DateTimeOffset.UtcNow), ct);
        await roles.Add(role, id, ct);
    }

    private Task<Post> Topic(string tangent, string title, string body, string author, DateTimeOffset at, int clock, CancellationToken ct)
        => Write(new StartTopic(Tid(at, clock), tangent, title, body, author, At: at), ct);

    private Task<Post> Answer(Post topic, string body, string author, DateTimeOffset at, int clock, CancellationToken ct)
        => Write(new Reply(Tid(at, clock), topic.Id, body, author, At: at), ct);

    private Task Seen(Post topic, long sequence, CancellationToken ct)
        => Write(new Keep<Checkpoint>(Checkpoint.At(Persona, Scope.Topic(topic.Id), sequence)), ct);

    private static Post Pinned(Post? root)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.Pinned = true;
        return root;
    }

    private async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await writer.Send(command, ct) switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The capture seed's {command.GetType().Name} answered {other}.")
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
