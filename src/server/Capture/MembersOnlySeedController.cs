using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Capture;

/// <summary>The rows the members-only captures show, sent as commands when the capture asks: three members-only
/// Tangents beside a public one — one anyone joins, one people ask to join, one where the persona's ask waits — and, for
/// the people tab, the persona an administrator, with Leo let in to the workshop and Mara and Lumen asking. Compiled only
/// into the capture host, like <see cref="CaptureSeedController"/>; fixed names.</summary>
public sealed class MembersOnlySeedController(RoleCollection roles) : ControllerBase
{
    private const string Persona = "did:plc:capture4persona";
    private const string Leo = "did:plc:capture4leo";
    private const string Mara = "did:plc:capture4mara";
    private const string Lumen = "did:plc:capture4lumen";

    [HttpPost("/capture/seed/members-only")]
    public Task<IActionResult> Outside(CancellationToken ct) => Seed(false, ct);

    [HttpPost("/capture/seed/members-only-people")]
    public Task<IActionResult> People(CancellationToken ct) => Seed(true, ct);

    private async Task<IActionResult> Seed(bool manager, CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Name = "Kintsugi Architecture";
        await Write(new Keep<TangentSpace>(space), ct);

        await Person(Leo, "leo.sylin.org", "Leo Botinelly", [Roles.Member, Roles.Administrator], ct);
        await Person(Mara, "mara.bsky.social", "Mara Lind", [Roles.Member], ct);
        await Person(Lumen, "lumen.agents.example", "Lumen", [Roles.Member], ct);
        await Person(Persona, "ada.bsky.social", "Ada Okafor", manager ? [Roles.Member, Roles.Administrator] : [Roles.Member], ct);

        var order = await Tangents.Tangent.NextOrder(ct);
        await Write(new CreateTangent("workshop", "The workshop", "Works in progress, shown to the people who are building them with us.", PostKind.Topic, order, Audience.Private, Leo), ct);
        await Write(new CreateTangent("off-the-record", "Off the record", "Loose talk among regulars, kept out of search and feeds.", PostKind.Topic, order + 1, Audience.Private, Leo), ct);
        await Write(new CreateTangent("reading-room", "The reading room", "Drafts of the long pieces before they go out.", PostKind.Article, order + 2, Audience.Private, Leo), ct);
        await Write(new ChangeJoining("workshop", Leo, Joining.Ask), ct);
        await Write(new ChangeJoining("off-the-record", Leo, Joining.Join), ct);
        await Write(new ChangeJoining("reading-room", Leo, Joining.Ask), ct);

        if (manager)
        {
            await Write(new JoinTangent("workshop", Mara), ct);
            await Write(new JoinTangent("workshop", Lumen), ct);
            await Write(new LetIn("off-the-record", Leo, Mara, true), ct);
            await Write(new LetIn("workshop", Leo, Leo, true), ct);
        }
        else
        {
            await Write(new JoinTangent("reading-room", Persona), ct);
        }

        return NoContent();
    }

    private async Task Person(string id, string handle, string displayName, IReadOnlyList<string> held, CancellationToken ct)
    {
        await Write(new Keep<Participant>(new Participant { Id = id, Handle = handle }), ct);
        await Write(new CaptureProfile(new ProfileAnswer(id, displayName, null, null), DateTimeOffset.UtcNow), ct);
        foreach (var role in held) await roles.Add(role, id, ct);
    }

    private async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await command.Send() switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The members-only seed's {command.GetType().Name} answered {other}.")
    };

    private sealed record Keep<TEntity>(TEntity Row) : ICommand<TEntity> where TEntity : Entity<TEntity>
    {
        public async Task<Outcome<TEntity>> Run(CancellationToken ct)
        {
            await Row.Save(ct);
            return new Outcome<TEntity>.Done(Row);
        }
    }
}
