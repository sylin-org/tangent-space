using System.Globalization;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
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

/// <summary>What every write of a Tangent does beside its row, inside the hold the row is written in: the one that creates
/// it (<see cref="TangentsController"/>, first run's <see cref="FirstTangent"/> and <see cref="ModeratorsTangent"/>), the one
/// that changes it, and the one that deletes it. Each refuses by throwing the <see cref="Refusal"/> its reason names, which
/// leaves nothing written.</summary>
public static class TangentWriting
{
    /// <summary>The answer to a delete of the moderators' Tangent.</summary>
    public static readonly Reason Kept = Reason.Invalid("The moderators’ Tangent stays: every report goes there.");

    /// <summary>The word of a delete whose count of Topics is not the Tangent's.</summary>
    public const string HoldsTopicsWord = "holds-topics";

    /// <summary>The most asks one read of a delete takes.</summary>
    public const int AskPage = 100;

    /// <summary>The most Tangents one read of a move takes: a page of the Tangents in their order.</summary>
    public const int OrderPage = 100;

    /// <summary>The answer to a delete that names another count of Topics than the <paramref name="count"/> the Tangent
    /// holds: what goes with it, read again before it is sent again.</summary>
    public static Reason HoldsTopics(int count) => Reason.Conflict(HoldsTopicsWord, count switch
    {
        0 => "It holds no topics now: read what goes with it, then delete it again.",
        1 => "It holds one topic now, which would go with it: read what goes with it, then delete it again.",
        _ => $"It holds {count.ToString(CultureInfo.InvariantCulture)} topics now, which would go with it: read what goes with it, then delete it again."
    });

    /// <summary>Opens <paramref name="tangent"/>, a Tangent about to be saved for the first time, with its
    /// <paramref name="audience"/>, written as its <c>everyone</c> overwrite over whatever overwrite of <c>everyone</c> its slug
    /// holds, and appends <see cref="EventKind.TangentChanged"/> carrying that audience, whose sequence the Tangent keeps
    /// (<see cref="Tangent.LogSeq"/>). Saving the row is the caller's.</summary>
    public static async Task Opened(Tangent tangent, Audience audience, string? actorId, DateTimeOffset at, CancellationToken ct)
    {
        await Overwrites.SetAudience(Roles(), tangent.Id, audience, ct);
        tangent.LogSeq = (await Event.Append(EventKind.TangentChanged, at, ct, tangentId: tangent.Id, actorId: actorId, audience: audience)).Seq;
    }

    /// <summary>Changes <paramref name="prior"/>, a Tangent as it stood, to <paramref name="working"/>, its row as the change
    /// leaves it, unsaved, by <paramref name="actorId"/>: its name kept trimmed and its accent trimmed, a text it holds as null
    /// read as none; its audience written as
    /// its <c>everyone</c> overwrite where <paramref name="audience"/> names another; and its place among the Tangents moved
    /// one place where <paramref name="move"/> says so (<see cref="Reorder"/>), which renumbers the others. The moderators'
    /// Tangent keeps its audience, its place and its joining (<see cref="Tangent.Fixed"/>). Its image's use moves with its image,
    /// the actor its holder, and one <see cref="EventKind.TangentChanged"/> carrying the audience is appended. Answers whether
    /// anything changed; a change that changes nothing writes nothing.</summary>
    public static async Task<bool> Changed(Tangent prior, Tangent working, Audience? audience, Move? move, string actorId, DateTimeOffset at,
        CancellationToken ct)
    {
        working.Name = (working.Name ?? "").Trim();
        working.Description ??= "";
        working.Accent = (working.Accent ?? "").Trim();
        working.Image ??= "";
        working.Image = Art.Judged(working.Image) ?? working.Image;
        var actual = await Overwrites.AudienceOf(prior.Id, ct);
        var moderators = (await TangentSpace.Get(TangentSpace.SingleRow, ct))?.ModeratorsTangentId;
        var audienceChanged = audience is { } wanted && wanted != actual;
        if (prior.Id == moderators && (move is not null || audienceChanged || working.Joining != prior.Joining)) throw new Refusal(Tangent.Fixed);

        var changed = working.Name != prior.Name || working.Description != prior.Description || working.DefaultKind != prior.DefaultKind
                      || working.Accent != prior.Accent || working.Image != prior.Image || working.Joining != prior.Joining;
        if (audienceChanged) await Overwrites.SetAudience(Roles(), prior.Id, audience!.Value, ct);
        var moved = move is { } way && await Reorder(working, way, moderators, ct);
        if (!changed && !moved && !audienceChanged) return false;

        await MediaUse.Record(prior.Id, prior.Image, working.Image, actorId, ct);
        await Event.Append(EventKind.TangentChanged, at, ct, tangentId: prior.Id, actorId: actorId, audience: audience ?? actual ?? Audience.Public);
        return true;
    }

    /// <summary>Deletes everything of <paramref name="tangent"/>, the row read in the hold, but the row itself, which the caller
    /// removes, by <paramref name="actorId"/>, where <paramref name="topics"/> is the count of Topics it holds
    /// (<see cref="Tangent.TopicCount"/>), or refuses with <see cref="HoldsTopics"/>, so a bare delete takes only an empty
    /// Tangent; the moderators' Tangent stays (<see cref="Kept"/>). Its Posts go with their snapshots, their media uses and the
    /// follows of its Topics (<see cref="DeleteTopic.Rows"/>), a page of <see cref="DeleteTopic.PostPage"/> at a time, each read
    /// finding what the last left, since a hold reads its own deletes; then its asks, every overwrite on its resource
    /// (<see cref="Overwrites.RemoveAll"/>) and its image's use; and <see cref="EventKind.TangentDeleted"/> is appended. Its rows
    /// of the log stay, and reach no one once it has no row (<see cref="Access.Capabilities.LoadEach"/>); a Connection takes down
    /// each copy of its articles from the copy's receipt (<see cref="Connections.Syndication"/>). The count is moved in the
    /// commit that starts, moves or deletes a Topic, a removed one still counted, and holds take turns, so a Topic started or
    /// moved in after the count was read refuses the delete. The order keeps its gap.</summary>
    public static async Task Deleting(Tangent tangent, int topics, string actorId, DateTimeOffset at, CancellationToken ct)
    {
        if ((await TangentSpace.Get(TangentSpace.SingleRow, ct))?.ModeratorsTangentId == tangent.Id) throw new Refusal(Kept);
        if (tangent.TopicCount != topics) throw new Refusal(HoldsTopics(tangent.TopicCount));

        var read = DeleteTopic.PostPage;
        while (read == DeleteTopic.PostPage)
        {
            var posts = await Post.Query(post => post.TangentId == tangent.Id, new QueryDefinition { Page = 1, PageSize = DeleteTopic.PostPage }, ct);
            read = posts.Count;
            if (read > 0) await DeleteTopic.Rows(posts, ct);
        }

        read = AskPage;
        while (read == AskPage)
        {
            var asks = await Ask.Query(ask => ask.TangentId == tangent.Id, new QueryDefinition { Page = 1, PageSize = AskPage }, ct);
            read = asks.Count;
            if (read > 0) await Ask.Remove(asks.Select(ask => ask.Id), ct);
        }

        await Overwrites.RemoveAll(Roles(), tangent.Id, ct);
        await MediaUse.Record(tangent.Id, tangent.Image, "", actorId, ct);
        await Event.Append(EventKind.TangentDeleted, at, ct, tangentId: tangent.Id, actorId: actorId);
    }

    /// <summary>Moves <paramref name="tangent"/> one place among the Tangents in their order, the moderators' Tangent
    /// left after every other: it and its neighbour that way change places, and the Tangents are numbered from one in that
    /// order, saving each other row whose order changes, up to the first Tangent past the pair whose stored order already comes
    /// after the last number given, which with every Tangent after it keeps its order; <paramref name="tangent"/> takes its
    /// number unsaved, as its write saves it. They are read a page of <see cref="OrderPage"/> at a time, each page continuing
    /// from the stored order and slug of the last Tangent the one before read, and saved once the reading is done, since a hold
    /// reads its own saves; so a move reads the Tangents before the pair and at most a page past it, however many the place
    /// holds. Answers false where it is already first or last, and nothing changes.</summary>
    private static async Task<bool> Reorder(Tangent tangent, Move move, string? moderators, CancellationToken ct)
    {
        var earlier = move == Move.Earlier;
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

        foreach (var row in renumbered.Where(row => row != tangent)) await row.Save(ct);
        return true;

        void Number(Tangent row)
        {
            number++;
            if (row.Order == number && row != tangent) return;
            row.Order = number;
            renumbered.Add(row);
        }
    }

    private static RoleCollection Roles() => AppHost.GetRequiredService<RoleCollection>(nameof(TangentWriting));
}
