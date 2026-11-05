using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>Which way a Tangent moves among the Tangents: one place earlier or one place later.</summary>
public enum Move
{
    Earlier,
    Later
}

/// <summary>Changes a Tangent's settings: its name, description and default kind (<see cref="Tangent.Change"/>; the
/// slug stays), its accent and image (<see cref="Tangent.Wear"/>), the image's use in step, its audience, written as its <c>everyone</c> overwrite as <see cref="CreateTangent"/> writes it, and its
/// order, one place earlier or later. A field left null stays as it is. The Tangent exists for the actor, who holds
/// <see cref="Bit.View"/> on it, or the command answers that no Tangent has the slug; the actor holds
/// <see cref="Bit.ManageTangent"/> there, which <see cref="Bit.ManageSpace"/> implies, and
/// <see cref="Bit.ManageSpace"/> in the place for a move, or is refused with the gate word. The moderators' Tangent's
/// audience and order are fixed (<see cref="ModeratorsTangent"/>): a command that would change either is refused with
/// <see cref="Fixed"/>. A change appends one <see cref="EventKind.TangentChanged"/> carrying the audience; a command that
/// changes nothing writes nothing.</summary>
public sealed record ChangeTangent(
    string Slug,
    string ActorId,
    string? Name = null,
    string? Description = null,
    PostKind? DefaultKind = null,
    Audience? Audience = null,
    Move? Move = null,
    string? Accent = null,
    string? Image = null,
    DateTimeOffset? At = null) : ICommand<Tangent>
{
    public static readonly Reason NoTangent = Reason.Absent("No Tangent has this slug.");

    /// <summary>The answer to a change of the moderators' Tangent's audience or order.</summary>
    public static readonly Reason Fixed = Reason.Invalid("The moderators’ Tangent keeps its audience and its place: only moderators read it, and it comes after every other Tangent.");

    /// <summary>The most Tangents one read of a move takes: a page of the Tangents in their order.</summary>
    public const int OrderPage = 100;

    public async Task<Outcome<Tangent>> Run(CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is not { } tangent) return new Outcome<Tangent>.Refused(NoTangent);
        var actual = await Overwrites.AudienceOf(Slug, ct);
        // Judged before the audience moves, since a new audience can take the actor's own View away.
        if (await Judge(actual, ct) is { } refusal) return new Outcome<Tangent>.Refused(refusal);

        var wore = tangent.Image;
        var changed = tangent.Change(Name ?? tangent.Name, Description ?? tangent.Description, DefaultKind ?? tangent.DefaultKind) | tangent.Wear(Accent, Image);
        var audienceChanged = Audience is { } wanted && actual != wanted;
        if (audienceChanged) await Overwrites.SetAudience(Collection(), Slug, Audience!.Value, ct);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct);
        var moved = Move is { } move && await Reorder(tangent, move, space?.ModeratorsTangentId, ct);
        if (!changed && !moved && !audienceChanged) return new Outcome<Tangent>.Done(tangent);

        if (!moved) await tangent.Save(ct);
        await MediaUse.Record(tangent.Id, wore, tangent.Image, ActorId, ct);
        await Event.Append(EventKind.TangentChanged, At ?? DateTimeOffset.UtcNow, ct, tangentId: tangent.Id, actorId: ActorId,
            audience: Audience ?? actual ?? Space.Audience.Public);
        return new Outcome<Tangent>.Done(tangent);
    }

    /// <summary>Null where the actor may make this change; otherwise why not: the Tangent does not exist for them; they
    /// may not manage it (<see cref="Bit.ManageTangent"/>); a move, which arranges the place's list, without
    /// <see cref="Bit.ManageSpace"/> in the place; or a change of the moderators' Tangent's audience or order
    /// (<see cref="Fixed"/>), judged against the audience Koan's rows state, <paramref name="actual"/>.</summary>
    private async Task<Reason?> Judge(Space.Audience? actual, CancellationToken ct)
    {
        var capabilities = await Capabilities.Load(ActorId, Slug, null, ct);
        if (!capabilities.Exists) return NoTangent;
        if (capabilities.Gate(Bit.ManageTangent) is { } gate) return gate;
        if (Move is not null && (await Capabilities.Load(ActorId, null, null, ct)).Gate(Bit.ManageSpace) is { } arranging) return arranging;
        var moderators = (await TangentSpace.Get(TangentSpace.SingleRow, ct))?.ModeratorsTangentId == Slug;
        return moderators && (Move is not null || (Audience is { } asked && asked != actual)) ? Fixed : null;
    }

    /// <summary>Moves <paramref name="tangent"/> one place among the Tangents in their order, the moderators' Tangent
    /// left after every other: it and its neighbour that way change places, and the Tangents are numbered from one in that
    /// order, saving each row whose order changes, the moved one among them, up to the first Tangent past the pair whose
    /// stored order already comes after the last number given, which with every Tangent after it keeps its order. They are
    /// read a page of <see cref="OrderPage"/> at a time, each page continuing from the stored order and slug of the last
    /// Tangent the one before read, and saved once the reading is done, since a command reads its own saves; so a move reads
    /// the Tangents before the pair and at most a page past it, however many the place holds. Answers false where it is
    /// already first or last, and nothing is saved.</summary>
    private static async Task<bool> Reorder(Tangent tangent, Move move, string? moderators, CancellationToken ct)
    {
        var earlier = move == Tangents.Move.Earlier;
        if ((await Tangent.InOrder((tangent.Order, tangent.Id), earlier, moderators, 1, ct)).FirstOrDefault() is not { } neighbour) return false;
        var (first, second) = earlier ? (tangent, neighbour) : (neighbour, tangent);
        var renumbered = new List<Tangent>();
        var number = 0;
        var placed = false;
        var reading = true;
        (int Order, string Id)? last = null;
        while (reading)
        {
            var page = await Tangent.InOrder(last, earlier: false, moderators, OrderPage, ct);
            if (page.Count > 0) last = (page[^1].Order, page[^1].Id);
            reading = page.Count == OrderPage;
            foreach (var row in page)
            {
                if (row.Id == second.Id) continue;
                if (row.Id == first.Id)
                {
                    Number(first);
                    Number(second);
                    placed = true;
                    continue;
                }

                if (placed && row.Order > number)
                {
                    reading = false;
                    break;
                }

                Number(row);
            }
        }

        foreach (var row in renumbered) await row.Save(ct);
        return true;

        void Number(Tangent row)
        {
            number++;
            if (row.Order == number && row != tangent) return;
            row.Order = number;
            renumbered.Add(row);
        }
    }

    private static RoleCollection Collection() => AppHost.GetRequiredService<RoleCollection>(nameof(ChangeTangent));
}
