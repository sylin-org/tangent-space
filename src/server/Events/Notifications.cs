using System.Globalization;
using System.Security.Cryptography;
using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;
using Microsoft.AspNetCore.DataProtection;

namespace Tangent.Events;

/// <summary>What is for one participant: every event that names them as its target, that someone else did, and that
/// reaches them by the stream's rule (<see cref="Readable"/>), so a row in a Tangent they may not view is absent. Read
/// through <c>ix_event_targets</c> (<c>TargetId</c>, <c>Seq</c>), newest first, one bounded range at a time: a page of
/// them continued from a sealed <see cref="Cursor"/>, and how many lie past their notifications checkpoint
/// (<see cref="Scope.Notifications"/>), counted among at most <see cref="CountLimit"/> and one more rows and said as a
/// lower bound when those that reach the reader fill them. Nothing it says of the count — the number, its bound, the
/// sequence it read through — moves for a row the reader may not see. Reads only and takes no lock; moving the checkpoint
/// is <see cref="SeeNotifications"/>'s.</summary>
public static class Notifications
{
    /// <summary>The address of a participant's notifications under the base URL.</summary>
    public const string Path = "/notifications";

    /// <summary>The most notifications one page holds.</summary>
    public const int PageSize = 20;

    /// <summary>The most rows one page reads, so a run of rows the reader may not see ends a page rather than
    /// lengthening it.</summary>
    public const int ScanLimit = 100;

    /// <summary>The highest count said as it is; past it the count reads "at least" (<c>99+</c>).</summary>
    public const int CountLimit = 99;

    /// <summary>The Data Protection purpose a page's cursor is sealed under.</summary>
    public const string CursorPurpose = "Notifications.Cursor";

    /// <summary>How many notifications the reader has not seen: <see cref="Count"/> past their notifications checkpoint,
    /// a lower bound where <see cref="AtLeast"/>; where they are (<see cref="Address"/>); and <see cref="Through"/>, the
    /// newest notification the count found, or the checkpoint where it found none, past which a row the page's stream
    /// delivers that is a notification for the reader adds one.</summary>
    public sealed record Unseen(int Count, bool AtLeast, string Address, long Through)
    {
        /// <summary>The count as the frame shows it: the number, with a plus where it is a lower bound, and
        /// <c>99+</c> past <see cref="CountLimit"/>.</summary>
        public string Badge => Count > CountLimit
            ? CountLimit.ToString(CultureInfo.InvariantCulture) + "+"
            : Count.ToString(CultureInfo.InvariantCulture) + (AtLeast ? "+" : "");
    }

    /// <summary>A page of notifications, newest first, and the sequence the next one reads before; null on the last.</summary>
    public sealed record Page(IReadOnlyList<Event> Events, long? Next);

    /// <summary>Where a page of notifications continues: before the sequence <see cref="Before"/>, marking new what lies
    /// past <see cref="From"/>, the checkpoint the first page was read from. It leaves the server only sealed.</summary>
    public sealed record Cursor(long Before, long From)
    {
        /// <summary>The cursor as an address carries it: protected under <see cref="CursorPurpose"/>, so it says nothing
        /// of the rows it passed and is never accepted altered.</summary>
        public string Seal(IDataProtectionProvider protection)
            => protection.CreateProtector(CursorPurpose)
                .Protect(Before.ToString(CultureInfo.InvariantCulture) + "\n" + From.ToString(CultureInfo.InvariantCulture));

        /// <summary>The cursor a sealed text holds, or null when the text is not one this server sealed.</summary>
        public static Cursor? Open(string? text, IDataProtectionProvider protection)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string plain;
            try { plain = protection.CreateProtector(CursorPurpose).Unprotect(text); }
            catch (CryptographicException) { return null; }
            var parts = plain.Split('\n');
            return parts.Length == 2
                   && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var before)
                   && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var from)
                ? new Cursor(before, from)
                : null;
        }
    }

    /// <summary>Whether <paramref name="row"/> is a notification for <paramref name="participantId"/>: it names them as
    /// its target, someone else did it, and it reaches them; <paramref name="readable"/> has read what that needs.</summary>
    public static bool For(Event row, string participantId, Readable readable)
        => row.TargetId == participantId && row.ActorId != participantId && readable.Reaches(row);

    /// <summary>The participant's notifications checkpoint: the newest sequence they have seen there, zero before any.</summary>
    public static async Task<long> Seen(string participantId, CancellationToken ct)
        => (await Checkpoint.Get(Checkpoint.Key(participantId, Scope.Notifications), ct))?.Seq ?? 0;

    /// <summary>How many notifications <paramref name="participantId"/> has not seen, past their checkpoint, with their
    /// address under <paramref name="baseAddress"/>.</summary>
    public static async Task<Unseen> Count(string participantId, string baseAddress, CancellationToken ct)
        => await CountPast(participantId, await Seen(participantId, ct), baseAddress, ct);

    /// <summary>How many notifications <paramref name="participantId"/> holds past <paramref name="since"/>: one read of at
    /// most <see cref="CountLimit"/> and one more rows, newest first, and what capability reads for the Tangents they
    /// name. Only the rows that reach the reader count: the count is a lower bound where they alone fill the read, and it
    /// reads through the newest of them, or stays at <paramref name="since"/> where none is, so a read holding rows the
    /// reader may not see counts fewer than may follow and says nothing of them.</summary>
    public static async Task<Unseen> CountPast(string participantId, long since, string baseAddress, CancellationToken ct)
    {
        var rows = await Event.Query(e => e.TargetId == participantId && e.Seq > since,
            new QueryDefinition { Page = 1, PageSize = CountLimit + 1 }.WithSort<Event>(sort => sort.OrderByDescending(e => e.Seq)), ct);
        var readable = new Readable(participantId);
        await readable.Prepare(rows, ct);
        var notifications = rows.Where(row => For(row, participantId, readable)).ToList();
        return new Unseen(notifications.Count, notifications.Count > CountLimit, baseAddress + Path,
            notifications.Count > 0 ? Math.Max(since, notifications[0].Seq) : since);
    }

    /// <summary>One page of <paramref name="participantId"/>'s notifications before <paramref name="before"/>, or the
    /// newest: one read of at most <see cref="ScanLimit"/> rows, newest first, keeping the notifications among them up to
    /// <see cref="PageSize"/>. The next page continues from the last row this one passed, so a row the reader may not
    /// see is read once and never named, and a full read that held fewer notifications than a page continues past it.</summary>
    public static async Task<Page> Read(string participantId, long? before, CancellationToken ct)
    {
        var bound = before ?? long.MaxValue;
        var rows = await Event.Query(e => e.TargetId == participantId && e.Seq < bound,
            new QueryDefinition { Page = 1, PageSize = ScanLimit }.WithSort<Event>(sort => sort.OrderByDescending(e => e.Seq)), ct);
        var readable = new Readable(participantId);
        await readable.Prepare(rows, ct);

        var kept = new List<Event>(PageSize);
        var passed = 0;
        foreach (var row in rows)
        {
            if (kept.Count == PageSize) break;
            passed++;
            if (For(row, participantId, readable)) kept.Add(row);
        }

        var more = passed < rows.Count || rows.Count == ScanLimit;
        return new Page(kept, more && passed > 0 ? rows[passed - 1].Seq : null);
    }
}
