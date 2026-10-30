using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Events;

/// <summary>The reader's notifications, one resource both ways: a page of <see cref="Entries"/>, newest first, each new
/// where it lies past <see cref="From"/>, the notifications checkpoint the reading began from; <see cref="Through"/>, where
/// this page leaves that checkpoint: the newest it shows where the person's own opening marked them seen, else where it
/// stood; <see cref="Next"/>, the address of the older page, null on the last,
/// and <see cref="Continued"/> for a page that follows another, whose newest is at <see cref="Address"/>. <see cref="Who"/>
/// is the signed-in reader and <see cref="Day"/> the UTC day the times are written for.</summary>
public sealed record NotificationsPage(
    string Place,
    string Door,
    string Address,
    long From,
    long Through,
    IReadOnlyList<NotificationsPage.Entry> Entries,
    string? Next,
    bool Continued,
    Who? Who,
    DateOnly Day)
{
    /// <summary>What a notification says happened, as <see cref="Entry.Kind"/> writes it.</summary>
    public static class Kinds
    {
        /// <summary>A reply to the reader.</summary>
        public const string Reply = "reply";

        /// <summary>A Topic taken on a tangent from the reader's Post.</summary>
        public const string Taken = "taken";

        /// <summary>A mention of the reader.</summary>
        public const string Mention = "mention";

        /// <summary>A report someone made, for whoever may view the moderators' Tangent, the reader among them.</summary>
        public const string Report = "report";

        /// <summary>A change of the reader's standing someone else made.</summary>
        public const string Standing = "standing";

        /// <summary>A role someone gave the reader or took from them.</summary>
        public const string Role = "role";

        /// <summary>An Offer the reader decides.</summary>
        public const string Offer = "offer";

        /// <summary>An application that waits for whoever decides it, the reader among them.</summary>
        public const string Application = "application";
    }

    /// <summary>One notification: the event's sequence, what happened (one of <see cref="Kinds"/>), whether it is new to the
    /// reader, when, who did it where the page names them, and the one address it is about: the Post, the Topic, the
    /// report in the moderators' Tangent, the door, the reader's profile, where the place answers, or the applications,
    /// for one who applied. A Post's carries its <see cref="Topic"/>, its <see cref="Tangent"/>, its opening words
    /// (<see cref="Line"/>) and whether it is <see cref="Removed"/>, and a report's also the author of the Post it reports
    /// (<see cref="Reported"/>), only where the reader may view that Post; a standing's the standing it left and the one
    /// before (<see cref="Was"/>); a role's the role and whether it was <see cref="Given"/>; an Offer's the Offer as it
    /// stands.</summary>
    public sealed record Entry(long Seq, string Kind, bool New, DateTimeOffset At, TangentPage.Author? Who, string? Address)
    {
        public string? Topic { get; init; }

        public string? Tangent { get; init; }

        public string? Line { get; init; }

        public bool? Removed { get; init; }

        public TangentPage.Author? Reported { get; init; }

        public string? Standing { get; init; }

        public string? Was { get; init; }

        public string? Role { get; init; }

        public bool? Given { get; init; }

        public Offered? Offer { get; init; }
    }

    /// <summary>An Offer as its decider reads it among their notifications: its Id, what it would do (<c>address</c>: use
    /// <see cref="Subject"/>, an origin, as the place's address), where it stands (<c>open</c>, <c>accepted</c>,
    /// <c>declined</c> or <c>lapsed</c>) and when it was decided; while it is open, its two answers, each a form carrying
    /// the Id in its <c>offer</c> field.</summary>
    public sealed record Offered(string Id, string Kind, string Subject, string State, DateTimeOffset? Decided)
    {
        public Form? Use { get; init; }

        public Form? Decline { get; init; }
    }
}
