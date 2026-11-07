using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;

namespace Tangent.Participants;

/// <summary>A Bluesky account named by its handle where admission says apply, keyed by its DID: its holder arrives
/// admitted, into the member role, at their first sign-in, which uses it up (<see cref="FirstArrival"/>), and until then
/// whoever holds <c>ManageStanding</c> withdraws it. It keeps the handle as it was resolved, the name and picture the
/// account's public profile showed then, who named it and when.</summary>
public sealed class Invitation : Entity<Invitation>
{
    public string Handle { get; set; } = "";

    public string? Name { get; set; }

    public string? Avatar { get; set; }

    public string InvitedBy { get; set; } = "";

    /// <summary>In <c>ix_invitation_times</c>, so the invitations, newest first, are one range.</summary>
    [Index(Name = "ix_invitation_times")]
    public DateTimeOffset InvitedAt { get; set; }
}

/// <summary>The invitations of the place, one bounded page at a time, newest first, through <c>ix_invitation_times</c>,
/// continued from a <see cref="Cursor"/>; nothing is counted past what one read holds.</summary>
public static class Invitations
{
    /// <summary>The most invitations one page holds.</summary>
    public const int PageSize = 25;

    /// <summary>A page of invitations and where the next one starts; <see cref="Next"/> is null on the last page.</summary>
    public sealed record Page(IReadOnlyList<Invitation> Invited, Cursor? Next);

    /// <summary>The place of the last invitation a page held: when it was made, and the account's DID, which orders
    /// invitations made at the same instant.</summary>
    public sealed record Cursor(DateTimeOffset At, string Did)
    {
        public static Cursor After(Invitation invitation) => new(invitation.InvitedAt, invitation.Id);

        /// <summary>The cursor a query carries, or null when the text is not one.</summary>
        public static Cursor? Parse(string? text)
        {
            var parts = text?.Split('.', 2) ?? [];
            if (parts.Length != 2 || !Participant.IsDid(parts[1])) return null;
            if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return null;
            return new Cursor(new DateTimeOffset(ticks, TimeSpan.Zero), parts[1]);
        }

        public override string ToString() => At.UtcTicks.ToString(CultureInfo.InvariantCulture) + "." + Did;
    }

    /// <summary>The page of invitations after <paramref name="after"/>, or the first: one read, limited to the page and one
    /// more, which says whether a next page exists. Past the cursor means made no later than it, and at its own instant,
    /// a DID before its.</summary>
    public static async Task<Page> Read(Cursor? after, CancellationToken ct)
    {
        var at = FieldPath.Of(nameof(Invitation.InvitedAt));
        var did = FieldPath.Of(nameof(Invitation.Id));
        var seek = after is null ? null : Filter.All(
            Filter.On(at, FilterOperator.Lte, FilterValue.Of(after.At)),
            Filter.Any(Filter.On(at, FilterOperator.Lt, FilterValue.Of(after.At)), Filter.On(did, FilterOperator.Lt, FilterValue.Of(after.Did))));
        var read = (await Invitation.All(new QueryDefinition { Filter = seek, Page = 1, PageSize = PageSize + 1 }
            .WithSort<Invitation>(sort => sort.OrderByDescending(invitation => invitation.InvitedAt).ThenByDescending(invitation => invitation.Id)), ct)).ToList();
        return read.Count > PageSize
            ? new Page(read.GetRange(0, PageSize), Cursor.After(read[PageSize - 1]))
            : new Page(read, null);
    }
}
