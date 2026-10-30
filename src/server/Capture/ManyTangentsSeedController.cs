using System.Globalization;
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

/// <summary>The rows the captures of a long list of Tangents show, written through the writer when the capture asks: a
/// place of sixty Tangents after its first, so the door, the switcher and the list of Tangents each hold more than one
/// page, and the capture persona a moderator, whose door lists the moderators' Tangent after the first page. Compiled
/// only into the capture host, like <see cref="CaptureSeedController"/>; fixed names.</summary>
public sealed class ManyTangentsSeedController(WriteLoop writer, RoleCollection roles) : ControllerBase
{
    private const string Persona = "did:plc:capture4persona";
    private const string Leo = "did:plc:capture4leo";

    /// <summary>How many Tangents the seed adds.</summary>
    public const int Count = 60;

    [HttpPost("/capture/seed/many-tangents")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Name = "Kintsugi Architecture";
        await Write(new Keep<TangentSpace>(space), ct);

        await Person(Leo, "leo.sylin.org", "Leo Botinelly", [Roles.Administrator], ct);
        await Person(Persona, "ada.bsky.social", "Ada Okafor", [Roles.Member, Roles.Moderator], ct);

        var order = await Tangents.Tangent.NextOrder(ct);
        for (var index = 1; index <= Count; index++)
        {
            var number = index.ToString("00", CultureInfo.InvariantCulture);
            await Write(new CreateTangent("tangent-" + number, "Tangent " + number, "", index % 5 == 0 ? PostKind.Article : PostKind.Topic,
                order + index - 1, Audience.Public, Leo), ct);
        }

        return NoContent();
    }

    private async Task Person(string id, string handle, string displayName, IReadOnlyList<string> held, CancellationToken ct)
    {
        // Captured now, so a sign-in finds the profile fresh and no profile job reaches the network.
        await Write(new Keep<Participant>(new Participant { Id = id, Handle = handle }), ct);
        await Write(new CaptureProfile(new ProfileAnswer(id, displayName, null, null), DateTimeOffset.UtcNow), ct);
        foreach (var role in held) await roles.Add(role, id, ct);
    }

    private async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await writer.Send(command, ct) switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The many-Tangents seed's {command.GetType().Name} answered {other}.")
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
