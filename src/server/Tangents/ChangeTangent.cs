using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
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
/// <see cref="Bit.ManageTangent"/> or <see cref="Bit.ManageSpace"/> there (<see cref="TangentWrites.MayManage"/>), and
/// <see cref="Bit.ManageSpace"/> in the place for a move, or is refused with the gate word. The moderators' Tangent's
/// audience and order are fixed (<see cref="ModeratorsTangent"/>): a command that would change either is refused with
/// <see cref="Fixed"/>. A change appends one <see cref="EventKind.TangentChanged"/> carrying the audience, and so does
/// an audience Koan's rows state that the log has not recorded; a command that changes nothing writes nothing.</summary>
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

    /// <summary>The most Tangents one read of a move takes: a page of the Tangents in their order.</summary>
    public const int OrderPage = 100;

    /// <summary>Whether <see cref="Prepare"/> judged the actor may make this change, before any audience it wrote could
    /// take their own <see cref="Bit.View"/> away; <see cref="Run"/> follows in the same turn of the loop and keeps that
    /// judgement.</summary>
    private bool _judged;

    /// <summary>Whether <see cref="Prepare"/> wrote the audience, which <see cref="Run"/> then records in the log.</summary>
    private bool _audienceWritten;

    /// <summary>Judges the change and writes the audience, before the transaction opens, since Koan commits an overwrite
    /// only outside one; only where <see cref="Run"/> goes on to change the Tangent — it exists for the actor, who may
    /// make this change (<see cref="Judge"/>), and its name and description are a Tangent's — and the audience differs
    /// from what Koan's rows state, so a command <see cref="Run"/> refuses changes nothing.</summary>
    public async Task Prepare(CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is not { } tangent) return;
        var roles = Collection();
        var actual = await Overwrites.AudienceOf(Slug, ct);
        if (await Judge(actual, ct) is not null) return;
        try { tangent.Change(Name ?? tangent.Name, Description ?? tangent.Description, DefaultKind ?? tangent.DefaultKind); }
        catch (Refusal) { return; }
        _judged = true;
        if (Audience is not { } audience || actual == audience) return;
        await Overwrites.SetAudience(roles, Slug, audience, ct);
        _audienceWritten = true;
    }

    /// <summary>Changes the Tangent where <see cref="Prepare"/> judged the actor may, or where they may now, and records
    /// the audience Koan's rows state where this command wrote it or where the log last said another
    /// (<see cref="Tangent.LoggedAudience"/>): one an earlier command wrote and could not record, whoever sends this one. A
    /// row that knows no logged audience takes Koan's with the next change it saves, and appends nothing for it.</summary>
    public async Task<Outcome<Tangent>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is not { } tangent) return new Outcome<Tangent>.Refused(NoTangent);
        var actual = await Overwrites.AudienceOf(Slug, ct);
        if (!_judged && await Judge(actual, ct) is { } refusal) return new Outcome<Tangent>.Refused(refusal);

        var changed = tangent.Change(Name ?? tangent.Name, Description ?? tangent.Description, DefaultKind ?? tangent.DefaultKind);
        if (Audience is { } wanted && actual != wanted) return new Outcome<Tangent>.Refused(NoAudience);
        // A row that knows no logged audience was recorded by its own creation's event: it takes Koan's quietly.
        var recorded = actual is { } stated && (_audienceWritten || (tangent.LoggedAudience is { } logged && logged != stated));
        if (actual is not null) tangent.LoggedAudience = actual;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct);
        var moved = Move is { } move && await Reorder(tangent, move, space?.ModeratorsTangentId, ct);
        if (!changed && !moved && !recorded) return new Outcome<Tangent>.Done(tangent);

        if (!moved) await tangent.Save(ct);
        await Event.Next(sequences, EventKind.TangentChanged, At ?? DateTimeOffset.UtcNow, tangentId: tangent.Id, actorId: ActorId,
            audience: actual ?? Space.Audience.Public).Save(ct);
        return new Outcome<Tangent>.Done(tangent);
    }

    /// <summary>Null where the actor may make this change; otherwise why not: the Tangent does not exist for them; they
    /// may not manage it (<see cref="TangentWrites.ManageGate"/>); a move, which arranges the place's list, without
    /// <see cref="Bit.ManageSpace"/> in the place; or a change of the moderators' Tangent's audience or order
    /// (<see cref="Fixed"/>), judged against the audience Koan's rows state, <paramref name="actual"/>.</summary>
    private async Task<string?> Judge(Space.Audience? actual, CancellationToken ct)
    {
        var capabilities = await Capabilities.Load(ActorId, Slug, null, ct);
        if (!capabilities.Exists) return NoTangent;
        if (TangentWrites.ManageGate(capabilities) is { } gate) return gate;
        if (Move is not null && (await Capabilities.Load(ActorId, null, null, ct)).Gate(Bit.ManageSpace) is { } arranging) return arranging;
        var moderators = (await TangentSpace.Get(TangentSpace.SingleRow, ct))?.ModeratorsTangentId == Slug;
        return moderators && (Move is not null || (Audience is { } asked && asked != actual)) ? Fixed : null;
    }

    /// <summary>Moves <paramref name="tangent"/> one place among the Tangents in their order, the moderators' Tangent
    /// left after every other: it and its neighbour that way (<see cref="Neighbour"/>) change places, and the Tangents are
    /// numbered from one in that order, saving each row whose order changes, the moved one among them, up to the first
    /// Tangent past the pair whose order already comes after the last number given, which with every Tangent after it
    /// keeps its order. They are read a page of <see cref="OrderPage"/> at a time, each page continuing from the last
    /// Tangent of the one before, and the reading stops there, so a move reads the Tangents before the pair and at most a
    /// page past it, however many the place holds. Answers false where it is already first or last, and nothing is
    /// saved.</summary>
    private static async Task<bool> Reorder(Tangent tangent, Move move, string? moderators, CancellationToken ct)
    {
        if (await Neighbour(tangent, move, moderators, ct) is not { } neighbour) return false;
        var (first, second) = move == Tangents.Move.Earlier ? (tangent, neighbour) : (neighbour, tangent);
        var number = 0;
        var placed = false;
        Tangent? last = null;
        while (true)
        {
            var page = await Following(last, moderators, OrderPage, ct);
            foreach (var row in page)
            {
                if (row.Id == second.Id) continue;
                if (row.Id == first.Id)
                {
                    await Number(first);
                    await Number(second);
                    placed = true;
                    continue;
                }

                if (placed && row.Order > number) return true;
                await Number(row);
            }

            if (page.Count < OrderPage) return true;
            last = page[^1];
        }

        async Task Number(Tangent row)
        {
            number++;
            if (row.Order == number && row != tangent) return;
            row.Order = number;
            await row.Save(ct);
        }
    }

    /// <summary>The Tangent next to <paramref name="tangent"/> in the Tangents' order that way, the moderators' Tangent
    /// aside; null where there is none. One read of one row.</summary>
    private static async Task<Tangent?> Neighbour(Tangent tangent, Move move, string? moderators, CancellationToken ct)
    {
        var earlier = move == Tangents.Move.Earlier;
        return (await Read(Beyond(tangent, earlier), earlier, moderators, 1, ct)).FirstOrDefault();
    }

    /// <summary>The Tangents after <paramref name="last"/> in their order, or the first ones where it is null, the
    /// moderators' Tangent aside; at most <paramref name="take"/>.</summary>
    private static Task<IReadOnlyList<Tangent>> Following(Tangent? last, string? moderators, int take, CancellationToken ct)
        => Read(last is null ? null : Beyond(last, earlier: false), earlier: false, moderators, take, ct);

    private static async Task<IReadOnlyList<Tangent>> Read(Filter? filter, bool earlier, string? moderators, int take, CancellationToken ct)
    {
        var query = new QueryDefinition { Filter = filter, Page = 1, PageSize = take }.WithSort<Tangent>(sort =>
        {
            if (earlier) sort.OrderByDescending(row => row.Order).ThenByDescending(row => row.Id);
            else sort.OrderBy(row => row.Order).ThenBy(row => row.Id);
        });
        var aside = moderators ?? "";
        return await Tangent.Query(row => row.Id != aside, query, ct);
    }

    /// <summary>The Tangents before or after <paramref name="tangent"/> in the order of their order and then their
    /// Id.</summary>
    private static Filter Beyond(Tangent tangent, bool earlier)
    {
        var order = FieldPath.Of(nameof(Tangent.Order));
        var id = FieldPath.Of(nameof(Tangent.Id));
        var beyond = earlier ? FilterOperator.Lt : FilterOperator.Gt;
        return Filter.Any(
            Filter.On(order, beyond, FilterValue.Of(tangent.Order)),
            Filter.All(Filter.On(order, FilterOperator.Eq, FilterValue.Of(tangent.Order)), Filter.On(id, beyond, FilterValue.Of(tangent.Id))));
    }

    private static RoleCollection Collection() => AppHost.GetRequiredService<RoleCollection>(nameof(ChangeTangent));
}
