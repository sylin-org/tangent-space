using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace TangentSpace.Application;

/// <summary>
/// The durable receipt of one requested mutation. Its key binds the participant credential, the
/// participant and the requestId; the fingerprint captures the exact intended action. A receipt is
/// written before any side effect and kept afterwards.
/// </summary>
public sealed class OperationReceipt : Entity<OperationReceipt>
{
    public string CredentialId { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string TargetKey { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    /// <summary>The credential-scoped operation id used for the underlying domain write, where one exists.</summary>
    public string? NamespacedOperationId { get; set; }
    public string State { get; set; } = "pending";
    public string? ResultRef { get; set; }
    /// <summary>The exact result data JSON of a completed action; replays return it verbatim.</summary>
    public string? ResultData { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public static string Key(string credentialId, string participantId, string requestId)
        => Hash(credentialId + "\n" + participantId + "\n" + requestId);

    /// <summary>The operation id namespaced to this credential and participant, so the same requestId under
    /// another credential can never collide in the underlying write keys.</summary>
    public static string BuildOperationId(string credentialId, string participantId, string requestId)
        => "op-" + Hash(credentialId + "\n" + participantId + "\n" + requestId)[..24];

    public static string FingerprintOf(string operation, string targetKey, string canonicalPayload)
        => Hash(operation + "\n" + targetKey + "\n" + canonicalPayload);

    public static void CheckRequestId(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64
            || requestId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Use a requestId of 1–64 letters, digits, hyphens or underscores.");
    }

    public bool Matches(string operation, string targetKey, string fingerprint)
        => Operation == operation && TargetKey == targetKey && Fingerprint == fingerprint;

    public void Complete(string state, string? resultRef, string? resultData, DateTimeOffset now)
    {
        if (State is "completed" or "rejected") return;
        State = state;
        ResultRef = resultRef ?? ResultRef;
        ResultData = resultData ?? ResultData;
        CompletedAt ??= now;
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
