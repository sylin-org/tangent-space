using System.Globalization;
using System.Security.Cryptography;
using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;
using Microsoft.AspNetCore.DataProtection;
using Tangent.Access;
using Tangent.Participants;

namespace Tangent.Events;

/// <summary>What is for one participant: every event that names them as its target, that someone else did, and that
/// reaches them by the stream's rule (<see cref="Readable"/>), so a row in a Tangent they may not view is absent; and, for
/// whoever holds <see cref="Bit.ManageStanding"/> in the place when they read, every application that waits, at its
/// arrival's place in the log (<see cref="Applications.Waiting"/>), so a decision by any decider ends it for all of them
/// and one who came to decide after it arrived reads it too. The events are read through <c>ix_event_targets</c>
/// (<c>TargetId</c>, <c>Seq</c>) and the applications through <c>ix_participant_waiting</c>, newest first, one bounded range
/// of each at a time: a page of them continued from a sealed <see cref="Cursor"/>, and how many lie past their
/// notifications checkpoint (<see cref="Scope.Notifications"/>), counted among at most <see cref="CountLimit"/> and one more
/// rows of each and said as a lower bound when those that reach the reader fill one. Nothing it says of the count, the
/// number, its bound or the sequence it read through, moves for a row the reader may not see. Reads only and takes no
/// lock; moving the checkpoint is <see cref="SeeNotifications"/>'s.</summary>
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
    /// a lower bound where <see cref="AtLeast"/>; where they are (<see cref="Address"/>); <see cref="Through"/>, the
    /// newest notification the count found, or the checkpoint where it found none, past which a row the page's stream
    /// delivers that is a notification for the reader adds one; and whether the applications that wait count among them
    /// (<see cref="Applications"/>), as they do for whoever decides them, so an application the stream delivers adds one
    /// too.</summary>
    public sealed record Unseen(int Count, bool AtLeast, string Address, long Through, bool Applications = false)
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
    /// name; and, where they decide applications, one read of at most as many applications that wait past it. Only the
    /// rows that reach the reader count: the count is a lower bound where they alone fill a read, and it reads through the
    /// newest of them, or stays at <paramref name="since"/> where none is, so a read holding rows the reader may not see
    /// counts fewer than may follow and says nothing of them.</summary>
    public static async Task<Unseen> CountPast(string participantId, long since, string baseAddress, CancellationToken ct)
    {
        var rows = await Event.Query(e => e.TargetId == participantId && e.Seq > since,
            new QueryDefinition { Page = 1, PageSize = CountLimit + 1 }.WithSort<Event>(sort => sort.OrderByDescending(e => e.Seq)), ct);
        var readable = new Readable(participantId);
        await readable.Prepare(rows, ct);
        var notifications = rows.Where(row => For(row, participantId, readable)).ToList();
        var decides = await readable.Decides(ct);
        var waiting = decides ? await Applications.Waiting(since, long.MaxValue, CountLimit + 1, ct) : [];
        var newest = Math.Max(notifications.Count > 0 ? notifications[0].Seq : since, waiting.Count > 0 ? waiting[0].WaitingSeq!.Value : since);
        return new Unseen(notifications.Count + waiting.Count, notifications.Count > CountLimit || waiting.Count > CountLimit,
            baseAddress + Path, Math.Max(since, newest), decides);
    }

    /// <summary>One page of <paramref name="participantId"/>'s notifications before <paramref name="before"/>, or the
    /// newest: one read of at most <see cref="ScanLimit"/> rows that target them and, where they decide applications, one
    /// read of at most as many applications that wait with the rows of their arrivals, newest first, keeping the
    /// notifications among them up to <see cref="PageSize"/>. A full read stops where it reached, so the page passes
    /// nothing below the lowest row of one, where the other may hold rows it has not read. The next page continues from
    /// the last row this one passed, so a row the reader may not see is read once and never named, and a full read that
    /// held fewer notifications than a page continues past it.</summary>
    public static async Task<Page> Read(string participantId, long? before, CancellationToken ct)
    {
        var bound = before ?? long.MaxValue;
        var rows = await Event.Query(e => e.TargetId == participantId && e.Seq < bound,
            new QueryDefinition { Page = 1, PageSize = ScanLimit }.WithSort<Event>(sort => sort.OrderByDescending(e => e.Seq)), ct);
        var readable = new Readable(participantId);
        await readable.Prepare(rows, ct);
        var waiting = await readable.Decides(ct) ? await Applications.Waiting(0, bound, ScanLimit, ct) : [];
        var arrivals = await Arrivals(waiting, ct);

        var floor = Math.Max(rows.Count == ScanLimit ? rows[^1].Seq : 0, waiting.Count == ScanLimit ? waiting[^1].WaitingSeq!.Value : 0);
        var read = rows.Select(row => (Row: row, Notification: For(row, participantId, readable)))
            .Concat(arrivals.Select(row => (Row: row, Notification: true)))
            .Where(each => each.Row.Seq >= floor)
            .OrderByDescending(each => each.Row.Seq)
            .ToList();

        var kept = new List<Event>(PageSize);
        var passed = 0;
        foreach (var (row, notification) in read)
        {
            if (kept.Count == PageSize) break;
            passed++;
            if (notification) kept.Add(row);
        }

        var more = passed < read.Count || rows.Count == ScanLimit || waiting.Count == ScanLimit;
        return new Page(kept, more && passed > 0 ? read[passed - 1].Row.Seq : null);
    }

    /// <summary>The arrivals of the applications that wait, by the sequence each waits at, in one keyed read.</summary>
    private static async Task<IReadOnlyList<Event>> Arrivals(IReadOnlyList<Participant> waiting, CancellationToken ct)
    {
        if (waiting.Count == 0) return [];
        var applicants = waiting.ToDictionary(applicant => Event.Key(applicant.WaitingSeq!.Value), applicant => applicant.Id, StringComparer.Ordinal);
        return (await Event.Get(applicants.Keys, ct)).OfType<Event>()
            .Where(row => Readable.Application(row) && row.TargetId == applicants[row.Id]).ToList();
    }
}
