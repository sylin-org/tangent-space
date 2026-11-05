using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>How a participant outside a private Tangent comes in: only as its managers let them in (<see cref="Closed"/>),
/// at once (<see cref="Join"/>), or by asking, which someone who holds <see cref="Bit.ManageTangent"/> there decides
/// (<see cref="Ask"/>). A private Tangent that a participant may join or ask to join is listed to them, with what it is
/// about; a closed one stays absent to them.</summary>
public enum Joining
{
    Closed,
    Join,
    Ask
}

/// <summary>A participant's ask to join a private Tangent whose joining says ask: waiting until someone who manages the
/// Tangent lets them in, which removes it, or declines it, which keeps it, so they do not ask again. Keyed by the Tangent
/// and the asker (<see cref="Key"/>).</summary>
public sealed class Ask : Entity<Ask>
{
    /// <summary>The most asks one read takes, for the people tab and for the door.</summary>
    public const int ReadSize = 25;

    [Index(Name = "ix_ask_tangents", Order = 0)]
    public string TangentId { get; set; } = "";

    public string PersonId { get; set; } = "";

    [Index(Name = "ix_ask_waiting", Order = 0)]
    [Index(Name = "ix_ask_tangents", Order = 1)]
    public bool Declined { get; set; }

    [Index(Name = "ix_ask_waiting", Order = 1)]
    [Index(Name = "ix_ask_tangents", Order = 2)]
    public DateTimeOffset AskedAt { get; set; }

    public static string Key(string tangentId, string personId) => tangentId + "|" + personId;

    /// <summary>At most <paramref name="limit"/> of the asks that wait in one Tangent, or in every Tangent where
    /// <paramref name="tangentId"/> is null, newest first: one read.</summary>
    public static Task<IReadOnlyList<Ask>> Waiting(string? tangentId, int limit, CancellationToken ct)
    {
        var query = new QueryDefinition { Page = 1, PageSize = limit }.WithSort<Ask>(sort => sort.OrderByDescending(ask => ask.AskedAt));
        return tangentId is null
            ? Query(ask => !ask.Declined, query, ct)
            : Query(ask => ask.TangentId == tangentId && !ask.Declined, query, ct);
    }
}

/// <summary>Who is in a private Tangent by their own place: a participant's own overwrite on it that allows
/// <see cref="Bit.View"/>, written allowing reading, posting and starting Topics when they join or are let in, and removed
/// when they leave. Taking them out writes it denying <see cref="Bit.View"/>, which wins over what their roles allow there
/// and keeps them from joining or asking until they are let in again; it is done only where it keeps them out
/// (<see cref="NotKept"/>). No other store says who is in.</summary>
public static class Inside
{
    public static readonly Reason Closed = Reason.Gated("closed", "This Tangent takes in only the people its managers let in.");
    public static readonly Reason AskDeclined = Reason.Gated("declined", "Your ask to join was declined; whoever manages this Tangent can still let you in.");
    public static readonly Reason NotIn = Reason.Conflict("You are not in this Tangent by joining it, so there is nothing to leave.");
    public static readonly Reason NoOne = Reason.Invalid(TangentPeopleController.PersonField, "No one by that id has arrived here.");

    /// <summary>The answer to taking someone out of a public Tangent, which they read signed out whatever is written.</summary>
    public static readonly Reason Public = Reason.Conflict("public", "Everyone who reads the place reads a public Tangent, signed in or not: make it private to keep someone out.");

    /// <summary>The answer to taking oneself out.</summary>
    public static readonly Reason Themselves = Reason.Gated("themselves", "No one takes themselves out of a Tangent.");

    /// <summary>The answer to taking out someone whose role administers, which reads every Tangent.</summary>
    public static readonly Reason Administers = Reason.Gated("administers", "Someone who administers reads every Tangent, so no one keeps them out.");

    /// <summary>Why taking a participant out of a Tangent with this audience would not keep them out, or null where it would:
    /// no one takes themselves out (<see cref="Themselves"/>), everyone who reads the place reads a public Tangent, as one
    /// that states no audience is (<see cref="Public"/>), and someone who administers reads every Tangent
    /// (<see cref="Administers"/>).</summary>
    public static Reason? NotKept(Audience? audience, bool themselves, bool administers)
        => themselves ? Themselves : audience != Audience.Private ? Public : administers ? Administers : null;

    /// <summary>Whether asks to join a Tangent wait on whoever manages it: it is private, and its joining is not join, under
    /// which anyone joins at once. An ask that does not wait is shown nowhere and asks nothing of anyone; it waits again
    /// should the Tangent come back to asking.</summary>
    public static bool AsksWait(Tangent tangent, Audience? audience) => audience == Audience.Private && tangent.Joining != Joining.Join;

    /// <summary>The participant's own overwrite on the Tangent, one keyed read; null where there is none.</summary>
    public static Task<PermissionOverwrite?> Own(string tangentId, string personId, CancellationToken ct)
        => PermissionOverwrite.Get(PermissionOverwrite.KeyFor(Overwrites.Resource(tangentId), OverwriteSubjectKind.Person, personId), ct);

    /// <summary>Whether an overwrite lets its participant in: it allows <see cref="Bit.View"/>.</summary>
    public static bool In(PermissionOverwrite? own) => own is not null && Bits.InTokens(own.Allow).HasFlag(Bit.View);

    /// <summary>Whether an overwrite keeps its participant out: it denies <see cref="Bit.View"/>.</summary>
    public static bool Out(PermissionOverwrite? own) => own is not null && Bits.InTokens(own.Deny).HasFlag(Bit.View);

    /// <summary>Keeps the participant out: their own overwrite denies <see cref="Bit.View"/>, whatever their roles allow.</summary>
    public static Task KeepOut(RoleCollection roles, string tangentId, string personId, CancellationToken ct)
        => roles.Overwrite(Overwrites.Resource(tangentId), OverwriteSubjectKind.Person, personId, [], Bits.Tokens(Bit.View), ct: ct);

    /// <summary>Lets the participant in: their own overwrite allows reading, posting and starting Topics.</summary>
    public static Task LetIn(RoleCollection roles, string tangentId, string personId, CancellationToken ct)
        => roles.Overwrite(Overwrites.Resource(tangentId), OverwriteSubjectKind.Person, personId, Bits.Tokens(Bit.View | Bit.Post | Bit.Topic), [], ct: ct);

    /// <summary>Removes the participant's own overwrite, as leaving does.</summary>
    public static Task Remove(RoleCollection roles, string tangentId, string personId, CancellationToken ct)
        => roles.RemoveOverwrite(Overwrites.Resource(tangentId), OverwriteSubjectKind.Person, personId, ct);

    /// <summary>Whether a private Tangent is open to joining or asking: its joining is not closed, and it is not the
    /// moderators' Tangent.</summary>
    public static bool Joinable(Tangent tangent, string? moderatorsTangentId) => tangent.Joining != Joining.Closed && tangent.Id != moderatorsTangentId;
}
