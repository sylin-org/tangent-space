using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;

namespace Tangent.Offers;

/// <summary>What an Offer would do, from a closed set.</summary>
public enum OfferKind
{
    /// <summary>Use the origin requests arrive on, the Offer's subject, as the place's address.</summary>
    Address
}

/// <summary>How the one who decides an Offer decided it.</summary>
public enum OfferState
{
    Accepted,
    Declined
}

/// <summary>An Offer as its decider decided it: something the place learned and offered to do, a prepared action of an
/// <see cref="OfferKind"/> on one <see cref="Subject"/>, accepted or declined once. An open Offer is never stored; it is
/// computed from what the place sees (<see cref="AddressOffers"/>), and this row is what deciding it writes. Its Id
/// derives from what it offers (<see cref="IdFor"/>), so a decided Offer is never offered again.</summary>
public sealed class Offer : Entity<Offer>
{
    public static readonly Reason NotOpen = Reason.Conflict("This offer is no longer open; nothing changed.");

    /// <summary>The Id of the Offer of <paramref name="kind"/> on <paramref name="subject"/>: its kind's word and the
    /// subject percent-encoded, so no part holds <c>/</c>.</summary>
    public static string IdFor(OfferKind kind, string subject) => Word(kind) + "/" + Uri.EscapeDataString(subject);

    /// <summary>A kind as the Id and the wire write it.</summary>
    public static string Word(OfferKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>The Offer of <paramref name="kind"/> on <paramref name="subject"/>, decided <paramref name="state"/> at
    /// <paramref name="at"/>.</summary>
    public static Offer Decided(OfferKind kind, string subject, OfferState state, DateTimeOffset at) => new()
    {
        Id = IdFor(kind, subject),
        Kind = kind,
        Subject = subject,
        State = state,
        DecidedAt = at
    };

    public OfferKind Kind { get; set; }

    /// <summary>What it would act on: for <see cref="OfferKind.Address"/>, an origin.</summary>
    public string Subject { get; set; } = "";

    public OfferState State { get; set; }

    public DateTimeOffset DecidedAt { get; set; }
}
