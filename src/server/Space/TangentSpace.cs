using Koan.Data.Core.Model;

namespace Tangent.Space;

/// <summary>The one settings row: the name of the place, the base URL it answers at, whose place it is, who answers
/// for it, who may read and how one joins, and how it looks.</summary>
public sealed class TangentSpace : Entity<TangentSpace>
{
    /// <summary>The identity of the single row.</summary>
    public const string SingleRow = "tangent-space";

    public TangentSpace() => Id = SingleRow;

    public string Name { get; set; } = "";

    /// <summary>Scheme, host and optional path; every absolute link derives from it.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The participant whose place this is and whose voice fronts it; null until first run declares one.</summary>
    public string? ProprietorId { get; set; }

    /// <summary>The accountable person, declared at first run; null until then.</summary>
    public string? OperatorId { get; set; }

    /// <summary>Who may read: anyone, or participants the place admitted.</summary>
    public Audience Audience { get; set; } = Audience.Public;

    /// <summary>How a participant becomes a member of a private place, or of a members-only Tangent.</summary>
    public Admission Admission { get; set; } = Admission.Join;

    /// <summary>Whether a newly admitted participant's Posts wait for approval.</summary>
    public bool NewAccountsAfterApproval { get; set; }

    public Appearance Appearance { get; set; } = new();

    public Slots Slots { get; set; } = new();

    /// <summary>Raised by every change to the row.</summary>
    public long Version { get; set; } = 1;
}
