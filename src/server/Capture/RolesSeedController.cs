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

/// <summary>The rows the roles, access and private-place captures show, written through the writer when the capture
/// asks, in a place its operator set up, by that operator: two roles of the place's own, the night shift, which moderates
/// and decides standing, and patrons, which grants nothing of its own; a public discussions Tangent and a private studio
/// whose member role reads and posts there, members-only, where patrons read alone; or the place made private. Compiled
/// only into the capture host, like <see cref="CaptureSeedController"/>; fixed names.</summary>
public sealed class RolesSeedController(WriteLoop writer, RoleCollection roles) : ControllerBase
{
    [HttpPost("/capture/seed/roles")]
    public async Task<IActionResult> RolesScreen(CancellationToken ct)
    {
        var keeper = await Keeper(ct);
        await Write(new CreateTangent("discussions", "Discussions", "Open questions, half-ideas and the loose threads of the work.", PostKind.Topic,
            1, Audience.Public, keeper), ct);
        await Write(new CreateTangent("studio", "The studio", "Work in progress, for members.", PostKind.Topic, 2, Audience.Private, keeper), ct);
        await Write(new CreateRole(keeper, "Night shift", Bit.Moderate | Bit.ManageStanding), ct);
        await Write(new CreateRole(keeper, "Patrons", Bit.None), ct);
        await Write(new ChangeAccess("studio", keeper, Access.Roles.Member, RoleAccess.Post), ct);
        await Write(new ChangeAccess("studio", keeper, "role:patrons", RoleAccess.Read), ct);
        await Person("did:plc:capture4noor", "noor.bsky.social", [Access.Roles.Member, "role:night-shift"], ct);
        return NoContent();
    }

    [HttpPost("/capture/seed/private-place")]
    public async Task<IActionResult> PrivatePlace(CancellationToken ct)
    {
        var keeper = await Keeper(ct);
        await Write(new CreateTangent("discussions", "Discussions", "Open questions, half-ideas and the loose threads of the work.", PostKind.Topic,
            1, Audience.Public, keeper), ct);
        await Write(new ChangeTangentSpace(keeper, "Kintsugi Architecture", Audience: Audience.Private), ct);
        return NoContent();
    }

    /// <summary>Who set the place up, its operator, who shapes everything here.</summary>
    private static async Task<string> Keeper(CancellationToken ct)
        => (await TangentSpace.Get(TangentSpace.SingleRow, ct))?.OperatorId ?? throw new InvalidOperationException("The place has no operator.");

    private async Task Person(string id, string handle, IReadOnlyList<string> held, CancellationToken ct)
    {
        await Write(new Keep<Participant>(new Participant { Id = id, Handle = handle, Standing = Standing.Admitted }), ct);
        foreach (var role in held) await roles.Add(role, id, ct);
    }

    private async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await writer.Send(command, ct) switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The roles seed's {command.GetType().Name} answered {other}.")
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
