using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Tangent.Writer;

namespace Tangent.Offers;

/// <summary>What an Offer would do, from a closed set.</summary>
public enum OfferKind
{
    /// <summary>Use the origin requests arrive on, the Offer's subject, as the place's address.</summary>
    Address
}

/// <summary>Where an Offer stands: open until its decider accepts or declines it, once, or until it lapses because
/// what it would do no longer can be done.</summary>
public enum OfferState
{
    Open,
    Accepted,
    Declined,
    Lapsed
}

/// <summary>Something the place learned and offers to do for the one who decides it: a prepared action of a
/// <see cref="OfferKind"/> on one <see cref="Subject"/>, decided once by <see cref="DeciderId"/>. Its Id derives from
/// what it offers (<see cref="IdFor"/>), so the same Offer is never made twice, whatever became of the first. Making one
/// appends <c>offer.made</c>, targeted at its decider; reading that event decides nothing, and the Offer stays open
/// until it is decided.</summary>
public sealed class Offer : Entity<Offer>
{
    /// <summary>The most open Offers one read takes.</summary>
    public const int OpenLimit = 8;

    public const string NotOpen = "This offer is no longer open; nothing changed.";

    /// <summary>The Id of the Offer of <paramref name="kind"/> on <paramref name="subject"/>: its kind's word and the
    /// subject percent-encoded, so no part holds <c>/</c>.</summary>
    public static string IdFor(OfferKind kind, string subject) => Word(kind) + "/" + Uri.EscapeDataString(subject);

    /// <summary>A kind as the Id and the wire write it.</summary>
    public static string Word(OfferKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>A new open Offer, made at <paramref name="at"/>.</summary>
    public static Offer Make(OfferKind kind, string subject, string deciderId, DateTimeOffset at) => new()
    {
        Id = IdFor(kind, subject),
        Kind = kind,
        Subject = subject,
        DeciderId = deciderId,
        State = OfferState.Open,
        MadeAt = at
    };

    public OfferKind Kind { get; set; }

    /// <summary>What it would act on: for <see cref="OfferKind.Address"/>, an origin.</summary>
    public string Subject { get; set; } = "";

    /// <summary>Who decides it: the operator, for every kind there is.</summary>
    [Index(Name = "ix_offer_open", Order = 1)]
    public string DeciderId { get; set; } = "";

    [Index(Name = "ix_offer_open", Order = 0)]
    public OfferState State { get; set; }

    public DateTimeOffset MadeAt { get; set; }

    /// <summary>When it was accepted, declined or lapsed; null while it is open.</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    public void Accept(DateTimeOffset at) => Decide(OfferState.Accepted, at);

    public void Decline(DateTimeOffset at) => Decide(OfferState.Declined, at);

    public void Lapse(DateTimeOffset at) => Decide(OfferState.Lapsed, at);

    private void Decide(OfferState state, DateTimeOffset at)
    {
        if (State != OfferState.Open) throw new Refusal(NotOpen);
        State = state;
        DecidedAt = at;
    }
}
