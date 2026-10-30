using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Capture;

/// <summary>The rows the people captures show, written through the writer when the capture asks: in the place the capture
/// persona set up, so she is its operator, an administrator, a moderator, members — one of them an agent, one timed out
/// until a fixed instant and one banned — and an applicant, who waits among the applications and is not among the
/// people. And what the persona's door shows of her own standing: timed out until a fixed instant, or lifted and made a
/// moderator. Compiled only into the capture host, like <see cref="CaptureSeedController"/>; fixed names, handles and
/// times.</summary>
public sealed class PeopleSeedController(WriteLoop writer, RoleCollection roles) : ControllerBase
{
    /// <summary>The capture persona of <c>src/client/capture/screens.ts</c>.</summary>
    private const string Persona = "did:plc:capture4persona";

    [HttpPost("/capture/seed/people")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Name = "Kintsugi Architecture";
        await Write(new Keep<TangentSpace>(space), ct);

        await Person("did:plc:capture4leo", "leo.sylin.org", "Leo Botinelly", ParticipantKind.Person, Standing.Admitted, null, [Roles.Member, Roles.Administrator], ct);
        await Person("did:plc:capture4noor", "noor.bsky.social", "Noor Haddad", ParticipantKind.Person, Standing.Admitted, null, [Roles.Member, Roles.Moderator], ct);
        await Person("did:plc:capture4lumen", "lumen.sylin.org", "Lumen", ParticipantKind.Agent, Standing.Admitted, null, [Roles.Member], ct);
        await Person("did:plc:capture4mara", "mara.bsky.social", "Mara Lind", ParticipantKind.Person, Standing.TimedOut,
            new DateTimeOffset(2030, 11, 2, 9, 0, 0, TimeSpan.Zero), [Roles.Member], ct);
        await Person("did:plc:capture4kai", "kai.example.com", "Kai", ParticipantKind.Person, Standing.Banned, null, [Roles.Member], ct);
        await Write(new Keep<Participant>(Participant.Arrive("did:plc:capture4wren", "wren.bsky.social", Admission.Apply,
            new DateTimeOffset(2026, 9, 29, 14, 5, 0, TimeSpan.Zero))), ct);
        return NoContent();
    }

    /// <summary>The capture persona timed out by the place's operator until a fixed instant, so her door says until
    /// when.</summary>
    [HttpPost("/capture/seed/timed-out")]
    public async Task<IActionResult> TimedOut(CancellationToken ct)
    {
        var keeper = await PersonaArrived(ct);
        await Write(new TimeOut(keeper, Persona, new DateTimeOffset(2030, 11, 2, 9, 0, 0, TimeSpan.Zero)), ct);
        return NoContent();
    }

    /// <summary>The capture persona timed out by the place's operator, who then lifted it and made her a moderator, so
    /// her door says she is back and what she was made.</summary>
    [HttpPost("/capture/seed/lifted")]
    public async Task<IActionResult> Lifted(CancellationToken ct)
    {
        var keeper = await PersonaArrived(ct);
        await Write(new TimeOut(keeper, Persona, DateTimeOffset.UtcNow.AddDays(1)), ct);
        await Write(new Lift(keeper, Persona), ct);
        await Write(new ChangeRole(keeper, Persona, Roles.Moderator, Give: true), ct);
        return NoContent();
    }

    /// <summary>The capture persona, a member who arrived and has been at her door before she signs in, and the operator
    /// who acts on her.</summary>
    private async Task<string> PersonaArrived(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        await Person(Persona, "ada.bsky.social", "Ada Okafor", ParticipantKind.Person, Standing.Admitted, null, [Roles.Member], ct);
        await Write(new Keep<Checkpoint>(Checkpoint.At(Persona, Scope.Place, 0)), ct);
        return space.OperatorId ?? throw new InvalidOperationException("The place has no operator.");
    }

    private async Task Person(string id, string handle, string displayName, ParticipantKind kind, Standing standing, DateTimeOffset? until,
        IReadOnlyList<string> held, CancellationToken ct)
    {
        // Captured now, so a sign-in finds the profile fresh and no profile job reaches the network.
        await Write(new Keep<Participant>(new Participant { Id = id, Handle = handle, Standing = standing, StandingUntil = until }), ct);
        await Write(new CaptureProfile(new ProfileAnswer(id, displayName, null, null, AgentLabel: kind == ParticipantKind.Agent), DateTimeOffset.UtcNow), ct);
        foreach (var role in held) await roles.Add(role, id, ct);
    }

    private async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await writer.Send(command, ct) switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The people seed's {command.GetType().Name} answered {other}.")
    };

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
