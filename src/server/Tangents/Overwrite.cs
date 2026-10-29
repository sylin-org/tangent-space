using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>A per-Tangent allow and deny for a role or a participant, keyed by Tangent and subject. The subject is a
/// role id or a participant id; <see cref="Everyone"/> is the base every bag holds, and its <see cref="View"/> is the
/// Tangent's audience.</summary>
public sealed class Overwrite : Entity<Overwrite>
{
    public const string Everyone = "everyone";

    /// <summary>The one bit an audience is.</summary>
    public const string View = "View";

    public static string Key(string tangentId, string subjectId) => tangentId + "/" + subjectId;

    [Index(Name = "ix_overwrite_tangents")]
    public string TangentId { get; set; } = "";

    public string SubjectId { get; set; } = "";

    public List<string> Allow { get; set; } = [];

    public List<string> Deny { get; set; } = [];

    /// <summary>The <see cref="Everyone"/> overwrite that gives a Tangent this audience.</summary>
    public static Overwrite ForAudience(string tangentId, Audience audience) => new()
    {
        Id = Key(tangentId, Everyone),
        TangentId = tangentId,
        SubjectId = Everyone,
        Allow = audience == Audience.Public ? [View] : [],
        Deny = audience == Audience.Public ? [] : [View]
    };

    /// <summary>The audience an <see cref="Everyone"/> overwrite states.</summary>
    public Audience AudienceStated() => Allow.Contains(View) && !Deny.Contains(View) ? Audience.Public : Audience.Private;
}
