using System.Text;
using System.Text.Json;
using Koan.Data.Core;
using Tangent.Infrastructure;

namespace Tangent.Application;

/// <summary>
/// Registers a mutation before its side effects and reconciles retries. Within one participant
/// credential a requestId names exactly one action; a different action under the same id is a
/// conflict. Registration through completion runs under a per-key in-process lock on top of the
/// durable insert-only key.
/// </summary>
public sealed class OperationReceipts(TimeProvider clock)
{
    private readonly SemaphoreSlim[] stripes = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly AsyncLocal<CommitBinding?> binding = new();

    private sealed class CommitBinding
    {
        public OperationReceipt? Receipt;
        public Func<object?, (string State, string? Reference, string Data)>? Map;
    }

    public sealed record Registration(OperationReceipt Record, bool Reused);

    /// <summary>Serializes register, replay, execute and complete for one request key, so concurrent
    /// retries of the same action cannot interleave. A fixed set of lock stripes bounds memory.</summary>
    public async Task<T> Run<T>(string credentialId, string participantId, string requestId, Func<Task<T>> action, CancellationToken ct)
    {
        OperationReceipt.CheckRequestId(requestId);
        var key = OperationReceipt.Key(credentialId, participantId, requestId);
        var stripe = stripes[Convert.ToInt32(key[..2], 16) % stripes.Length];
        await stripe.WaitAsync(ct);
        var previous = binding.Value;
        var current = new CommitBinding();
        binding.Value = current;
        using var observer = CommandCommit.Observe(async (result, token) =>
        {
            if (current.Receipt is not { } receipt || current.Map is not { } map) return;
            var value = map(result);
            receipt.Complete(value.State, value.Reference, value.Data, clock.GetUtcNow());
            await receipt.Save(token); // Staged in the domain transaction; a rollback includes this receipt.
        });
        try { return await action(); }
        finally { binding.Value = previous; stripe.Release(); }
    }

    /// <summary>Insert-only registration at the deterministic key. An existing receipt must match exactly;
    /// any divergence is a conflict, never a silent second action.</summary>
    public async Task<Registration> Register(string credentialId, string participantId, string requestId, string operation,
        string targetKey, IReadOnlyDictionary<string, string?> payload, CancellationToken ct)
    {
        OperationReceipt.CheckRequestId(requestId);
        var fingerprint = OperationReceipt.FingerprintOf(operation, targetKey, Canonical(payload));
        var id = OperationReceipt.Key(credentialId, participantId, requestId);
        using var fresh = EntityContext.NoCache();
        var existing = await OperationReceipt.Get(id, ct);
        if (existing is not null)
        {
            if (!existing.Matches(operation, targetKey, fingerprint)) throw new RequestConflictException(requestId);
            return new Registration(existing, true);
        }
        var receipt = new OperationReceipt
        {
            Id = id, CredentialId = credentialId, ParticipantId = participantId, RequestId = requestId,
            Operation = operation, TargetKey = targetKey, Fingerprint = fingerprint,
            NamespacedOperationId = OperationReceipt.BuildOperationId(credentialId, participantId, requestId),
            RegisteredAt = clock.GetUtcNow()
        };
        await receipt.Save(ct);
        return new Registration(receipt, false);
    }

    public async Task<OperationReceipt?> Find(string credentialId, string participantId, string requestId, CancellationToken ct)
    {
        OperationReceipt.CheckRequestId(requestId);
        using var fresh = EntityContext.NoCache();
        return await OperationReceipt.Get(OperationReceipt.Key(credentialId, participantId, requestId), ct);
    }

    public async Task Complete(OperationReceipt receipt, string state, string? resultRef, string? resultData, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var stored = await OperationReceipt.Get(receipt.Id, ct) ?? receipt;
        stored.Complete(state, resultRef, resultData, clock.GetUtcNow());
        // A completed receipt already proves the mutation. Its presentation may be enriched after
        // commit, such as with the newly visible Topic directory, without changing the outcome.
        if (stored.State == "completed" && state == "completed" && resultData is not null)
        { stored.ResultData = resultData; stored.ResultRef = resultRef ?? stored.ResultRef; }
        await stored.Save(ct);
    }

    public void CompleteWithDomain(OperationReceipt receipt, Func<object?, (string State, string? Reference, string Data)> map)
    {
        var current = binding.Value ?? throw new InvalidOperationException("Receipt binding requires a running command.");
        current.Receipt = receipt;
        current.Map = map;
    }

    /// <summary>Canonical payload text: stable key order, explicit nulls. Small fixed argument sets only.</summary>
    public static string Canonical(IReadOnlyDictionary<string, string?> payload)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var key in payload.Keys.OrderBy(key => key, StringComparer.Ordinal))
            {
                if (payload[key] is { } value) writer.WriteString(key, value);
                else writer.WriteNull(key);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
