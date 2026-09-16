namespace Tangent.Community;

public sealed record TopicAdministrationResult(
    bool Accepted,
    TopicDenial? Denial,
    string Reason,
    string TopicKey,
    long PolicyRevision,
    string AuditId);
