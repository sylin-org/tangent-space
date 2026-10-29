using Koan.Data.Core.Model;

namespace Tangent.Writer;

/// <summary>A row the writer's proofs write: who sent it, in what order they sent it, what it says, and the sequence the
/// writer gave it. It exists only here.</summary>
public sealed class Slip : Entity<Slip>
{
    public string Sender { get; set; } = "";

    public int Ordinal { get; set; }

    public string Body { get; set; } = "";

    public long Sequence { get; set; }

    /// <summary>A slip with these words, or a refusal when there are none.</summary>
    public static Slip Write(string id, string sender, int ordinal, string body, long sequence)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new Refusal(NeedsBody);
        return new Slip { Id = id, Sender = sender, Ordinal = ordinal, Body = body, Sequence = sequence };
    }

    public const string NeedsBody = "A slip needs a body.";
}

/// <summary>A second entity type the atomic proof saves beside a <see cref="Slip"/>. It exists only here.</summary>
public sealed class Stub : Entity<Stub>
{
    public string Note { get; set; } = "";
}
