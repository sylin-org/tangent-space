using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Tangents;

/// <summary>Which way a Tangent moves among the Tangents: one place earlier or one place later.</summary>
public enum Move
{
    Earlier,
    Later
}

/// <summary>Changes a Tangent's settings: its name, description and default kind (<see cref="Tangent.Change"/>; the
/// slug stays), its audience, written as its <c>everyone</c> overwrite as <see cref="CreateTangent"/> writes it, and its
/// order, one place earlier or later. A field left null stays as it is. The Tangent exists for the actor, who holds
/// <see cref="Bit.View"/> on it, or the command answers that no Tangent has the slug; the actor holds
/// <see cref="Bit.ManageTangent"/> or <see cref="Bit.ManageSpace"/> there (<see cref="TangentWrites.MayManage"/>), or is
/// refused with the gate word. The moderators' Tangent's audience and order are fixed (<see cref="ModeratorsTangent"/>):
/// a command that would change either is refused with <see cref="Fixed"/>. A change appends one
/// <see cref="EventKind.TangentChanged"/> carrying the audience; a command that changes nothing writes nothing.</summary>
public sealed record ChangeTangent(
    string Slug,
    string ActorId,
    string? Name = null,
    string? Description = null,
    PostKind? DefaultKind = null,
    Audience? Audience = null,
    Move? Move = null,
    DateTimeOffset? At = null) : ICommand<Tangent>
{
    public const string NoTangent = "No Tangent has this slug.";

    /// <summary>The answer to a change of the moderators' Tangent's audience or order.</summary>
    public const string Fixed = "The moderators’ Tangent keeps its audience and its place: only moderators read it, and it comes after every other Tangent.";

    /// <summary>The answer when the audience the command asks for is not what Koan's rows state as it runs; sending the
    /// command again writes the audience anew.</summary>
    public const string NoAudience = "The Tangent’s audience could not be put in place, so nothing was changed; send it again.";

    /// <summary>The most Tangents a move reads to renumber their order: a Tangent Space's bound on Tangents
    /// (ARCHITECTURE §5).</summary>
    public const int OrderRead = 1000;

    /// <summary>Whether <see cref="Prepare"/> wrote the audience, so <see cref="Run"/> appends the change even where
    /// nothing else changed.</summary>
    private bool _audienceWritten;

    /// <summary>Writes the audience, before the transaction opens, since Koan commits an overwrite only outside one;
    /// only where <see cref="Run"/> goes on to change the Tangent — it exists for the actor, who may manage it, it is not
    /// the moderators' Tangent, and its name and description are a Tangent's — and the audience differs from what Koan's
    /// rows state, so a command <see cref="Run"/> refuses changes nothing.</summary>
    public async Task Prepare(CancellationToken ct)
    {
        if (Audience is not { } audience) return;
        if (await Tangent.Get(Slug, ct) is not { } tangent) return;
        var capabilities = await Capabilities.Load(ActorId, Slug, null, ct);
        if (!capabilities.Exists || TangentWrites.ManageGate(capabilities) is not null) return;
        if ((await TangentSpace.Get(TangentSpace.SingleRow, ct))?.ModeratorsTangentId == Slug) return;
        try { tangent.Change(Name ?? tangent.Name, Description ?? tangent.Description, DefaultKind ?? tangent.DefaultKind); }
        catch (Refusal) { return; }
        var roles = Collection();
        if (await Overwrites.AudienceOf(roles, Slug, ct) == audience) return;
        await Overwrites.SetAudience(roles, Slug, audience, ct);
        _audienceWritten = true;
    }

    public async Task<Outcome<Tangent>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is not { } tangent) return new Outcome<Tangent>.Refused(NoTangent);
        var capabilities = await Capabilities.Load(ActorId, Slug, null, ct);
        if (!capabilities.Exists) return new Outcome<Tangent>.Refused(NoTangent);
        if (TangentWrites.ManageGate(capabilities) is { } gate) return new Outcome<Tangent>.Refused(gate);

        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct);
        var audience = await Overwrites.AudienceOf(Collection(), Slug, ct);
        if (space?.ModeratorsTangentId == Slug && (Move is not null || (Audience is { } asked && asked != audience)))
            return new Outcome<Tangent>.Refused(Fixed);

        var changed = tangent.Change(Name ?? tangent.Name, Description ?? tangent.Description, DefaultKind ?? tangent.DefaultKind);
        if (Audience is { } wanted && audience != wanted) return new Outcome<Tangent>.Refused(NoAudience);
        var moved = Move is { } move && await Reorder(tangent, move, space?.ModeratorsTangentId, ct);
        if (!changed && !moved && !_audienceWritten) return new Outcome<Tangent>.Done(tangent);

        if (changed && !moved) await tangent.Save(ct);
        await Event.Next(sequences, EventKind.TangentChanged, At ?? DateTimeOffset.UtcNow, tangentId: tangent.Id, actorId: ActorId,
            audience: audience ?? Space.Audience.Public).Save(ct);
        return new Outcome<Tangent>.Done(tangent);
    }

    /// <summary>Moves <paramref name="tangent"/> one place among the Tangents in their order, the moderators' Tangent
    /// left after every other, and numbers them from one in that order, saving each row whose order changes, the moved
    /// one among them; one bounded read. Answers false where it is already first or last, and nothing is saved.</summary>
    private static async Task<bool> Reorder(Tangent tangent, Move move, string? moderators, CancellationToken ct)
    {
        var rows = (await Tangent.All(new QueryDefinition { Page = 1, PageSize = OrderRead }
                .WithSort<Tangent>(sort => sort.OrderBy(row => row.Order).ThenBy(row => row.Id)), ct))
            .Where(row => row.Id != moderators)
            .Select(row => row.Id == tangent.Id ? tangent : row)
            .ToList();
        var from = rows.FindIndex(row => row.Id == tangent.Id);
        var to = from + (move == Tangents.Move.Earlier ? -1 : 1);
        if (from < 0 || to < 0 || to >= rows.Count) return false;
        (rows[from], rows[to]) = (rows[to], rows[from]);
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Order == index + 1 && rows[index] != tangent) continue;
            rows[index].Order = index + 1;
            await rows[index].Save(ct);
        }

        return true;
    }

    private static RoleCollection Collection() => AppHost.GetRequiredService<RoleCollection>(nameof(ChangeTangent));
}
