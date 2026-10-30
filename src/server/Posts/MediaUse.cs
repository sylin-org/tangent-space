using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;
using Tangent.Access;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>What lets someone see a piece of <see cref="Media"/>, one row each: a Post that uses it, as its cover or an
/// image in its body, in its Tangent; or a participant who brought it. An image reaches whoever may view a Tangent one
/// of its Posts is in, and whoever brought it while the place exists for them, and is absent to everyone else: the same
/// picture in a public and a private Tangent reads as it does in the public one, since whoever sent it there held it
/// already. Removing a Post, or an edit that drops the image, ends its use. Keyed <c>{media}/{post or participant}</c>,
/// one index range per image.</summary>
public sealed class MediaUse : Entity<MediaUse>
{
    /// <summary>How many rows one read of an image's uses holds.</summary>
    public const int ReadSize = 100;

    [Index(Name = "ix_media_use")]
    public string MediaId { get; set; } = "";

    /// <summary>The Post that uses it; null on the row of who brought it.</summary>
    public string? PostId { get; set; }

    /// <summary>The Tangent that Post is in; null on the row of who brought it.</summary>
    public string? TangentId { get; set; }

    public static string Key(string mediaId, string holder) => mediaId + "/" + holder;

    /// <summary>The row that says <paramref name="personId"/> brought <paramref name="mediaId"/>.</summary>
    public static MediaUse Brought(string mediaId, string personId) => new() { Id = Key(mediaId, personId), MediaId = mediaId };

    /// <summary>Whether the image reaches this reader, a visitor where <paramref name="personId"/> is null. Reads the
    /// row of who brought it, by key, then the image's uses a page at a time, each Tangent judged once from Koan's
    /// compiled state, stopping at the first the reader may view.</summary>
    public static async Task<bool> Reaches(string? personId, string mediaId, CancellationToken ct)
    {
        if (personId is not null && await Get(Key(mediaId, personId), ct) is not null
            && (await Capabilities.LoadRows(personId, null, ct)).For(null).Exists) return true;
        var judged = new HashSet<string>(StringComparer.Ordinal);
        string? after = null;
        while (true)
        {
            var query = new QueryDefinition
            {
                Filter = after is null ? null : Filter.On(FieldPath.Of(nameof(Id)), FilterOperator.Gt, FilterValue.Of(after)),
                Page = 1,
                PageSize = ReadSize
            }.WithSort<MediaUse>(sort => sort.OrderBy(use => use.Id));
            var uses = await Query(use => use.MediaId == mediaId, query, ct);
            foreach (var use in uses)
            {
                if (use.TangentId is not { } tangent || !judged.Add(tangent)) continue;
                if ((await Capabilities.LoadRows(personId, tangent, ct)).For(null).Exists) return true;
            }

            if (uses.Count < ReadSize) return false;
            after = uses[^1].Id;
        }
    }

    /// <summary>Inside the running command, brings the rows of <paramref name="post"/> in step with what it uses now:
    /// each image it no longer uses ends, and each it newly uses begins where <paramref name="actorId"/> may see it, so
    /// naming an image is no way to reach it. One that names an image they may not see stays words nobody can open.
    /// <paramref name="before"/> is what it used before this command.</summary>
    public static async Task Record(Post post, string actorId, IReadOnlyList<string> before, CancellationToken ct)
    {
        var now = post.UsedMedia();
        foreach (var gone in before.Except(now, StringComparer.Ordinal))
            if (await Get(Key(gone, post.Id), ct) is { } use) await use.Remove(ct);
        foreach (var added in now.Except(before, StringComparer.Ordinal))
            if (await Reaches(actorId, added, ct))
                await new MediaUse { Id = Key(added, post.Id), MediaId = added, PostId = post.Id, TangentId = post.TangentId }.Save(ct);
    }
}

/// <summary>Records who brought a piece of <see cref="Media"/>, so it reaches them before any Post uses it. Safe to
/// repeat.</summary>
public sealed record BringMedia(string MediaId, string PersonId) : ICommand<MediaUse>
{
    public async Task<Outcome<MediaUse>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await MediaUse.Get(MediaUse.Key(MediaId, PersonId), ct) is { } existing) return new Outcome<MediaUse>.Done(existing);
        var use = MediaUse.Brought(MediaId, PersonId);
        await use.Save(ct);
        return new Outcome<MediaUse>.Done(use);
    }
}
