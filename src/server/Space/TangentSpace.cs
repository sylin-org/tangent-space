using Koan.Data.Core.Model;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>The one settings row: the name of the place, whose place it is, who answers for it, who may read and how
/// one joins, and how it looks. Where it answers is configuration, Koan's <c>Koan:Web:ApplicationUrl</c>, never a copy
/// here. It exists once first run establishes it; until then the
/// place reads as <see cref="Unestablished"/>.</summary>
public sealed class TangentSpace : Entity<TangentSpace>
{
    /// <summary>The identity of the single row.</summary>
    public const string SingleRow = "tangent-space";

    /// <summary>The name a place shows before first run names it.</summary>
    public const string Unnamed = "New Tangent Space";

    /// <summary>The longest name a place takes.</summary>
    public const int NameLimit = 120;

    /// <summary>The refusal a name outside the rule meets.</summary>
    public const string NameRule = "Name the Tangent Space in 1 to 120 characters, on one line.";

    public TangentSpace() => Id = SingleRow;

    /// <summary>The row first run writes: the name, whose place it is and who answers for it, read by anyone and
    /// joined at once, with the moderators' Tangent first run creates beside it.</summary>
    public static TangentSpace Establish(string name, string proprietorId, string operatorId, string moderatorsTangentId)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > NameLimit || trimmed.Any(char.IsControl)) throw new Refusal(NameRule);
        return new TangentSpace
        {
            Name = trimmed,
            ProprietorId = proprietorId,
            OperatorId = operatorId,
            ModeratorsTangentId = moderatorsTangentId,
            Audience = Audience.Public,
            Admission = Admission.Join
        };
    }

    /// <summary>How the place reads before first run: unnamed, with the audience and admission it will be established
    /// with. Never saved.</summary>
    public static TangentSpace Unestablished() => new() { Name = Unnamed };

    public string Name { get; set; } = "";

    /// <summary>The participant whose place this is and whose voice fronts it; null until first run declares one.</summary>
    public string? ProprietorId { get; set; }

    /// <summary>The accountable person, declared at first run; null until then.</summary>
    public string? OperatorId { get; set; }

    /// <summary>The slug of the moderators' own Tangent, where a report starts a Topic (<see cref="ModeratorsTangent"/>);
    /// null only for a place established before first run created one, until the next start creates it.</summary>
    public string? ModeratorsTangentId { get; set; }

    /// <summary>Who may read: anyone, or participants the place admitted.</summary>
    public Audience Audience { get; set; } = Audience.Public;

    /// <summary>How a participant becomes a member of a private place, or of a members-only Tangent.</summary>
    public Admission Admission { get; set; } = Admission.Join;

    /// <summary>Whether a newly admitted participant's Posts wait for approval.</summary>
    public bool NewAccountsAfterApproval { get; set; }

    public Appearance Appearance { get; set; } = new();

    public Slots Slots { get; set; } = new();

    /// <summary>Records the moderators' Tangent of a place established without one, a change to the row.</summary>
    public void RecordModerators(string tangentId)
    {
        if (ModeratorsTangentId is not null) throw new InvalidOperationException("This Tangent Space already has its moderators' Tangent.");
        ModeratorsTangentId = tangentId;
        Version = checked(Version + 1);
    }

    /// <summary>Raised by every change to the row.</summary>
    public long Version { get; set; } = 1;
}
