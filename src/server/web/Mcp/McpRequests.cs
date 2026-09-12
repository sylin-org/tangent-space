using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Koan.Data.Core;
using TangentSpace.Infrastructure;

namespace TangentSpace.Mcp;

/// <summary>
/// Registers mutations before side effects and reconciles retries. Caller-scoped uniqueness: the same
/// requestId under one credential/DID can never name two different actions (request_conflict instead).
/// The raw MCP JSON-RPC id is never used as a durable key. Registration through completion runs under a
/// per-key in-process gate on top of the durable insert-only key (single-instance monolith).
/// </summary>
public sealed class McpRequests(TimeProvider clock)
{
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly AsyncLocal<CommitBinding?> binding = new();
    private sealed class CommitBinding
    {
        public McpRequestRecord? Record;
        public Func<object?, (string State, string? Reference, string Data)>? Map;
    }

    public sealed record Registration(McpRequestRecord Record, bool Reused);

    /// <summary>Serializes the full register/replay/execute/complete sequence for one request key so
    /// concurrent retries of the same intended action cannot interleave. A fixed set of stripes bounds memory even for many unique request IDs.</summary>
    public async Task<T> Run<T>(string credentialId, string did, string requestId, Func<Task<T>> action, CancellationToken ct)
    {
        McpRequestRecord.CheckRequestId(requestId);
        var hash = McpRequestRecord.Key(credentialId, did, requestId);
        var gate = gates[Convert.ToInt32(hash[..2], 16) % gates.Length];
        await gate.WaitAsync(ct);
        var previous = binding.Value;
        var current = new CommitBinding();
        binding.Value = current;
        using var observer = CommandCommit.Observe(async (result, token) =>
        {
            if (current.Record is not { } record || current.Map is not { } map) return;
            var value = map(result);
            record.Complete(value.State, value.Reference, value.Data, clock.GetUtcNow());
            await record.Save(token); // Staged in the domain transaction; rollback includes this receipt.
        });
        try { return await action(); }
        finally { binding.Value = previous; gate.Release(); }
    }

    /// <summary>Insert-only registration at the deterministic key. Existing records are compared exactly;
    /// any divergence is a conflict, never a silent second action.</summary>
    public async Task<Registration> Register(string credentialId, string did, string requestId, string operation,
        string targetKey, IReadOnlyDictionary<string, string?> payload, CancellationToken ct)
    {
        McpRequestRecord.CheckRequestId(requestId);
        var fingerprint = McpRequestRecord.FingerprintOf(operation, targetKey, Canonical(payload));
        var id = McpRequestRecord.Key(credentialId, did, requestId);
        using var fresh = EntityContext.NoCache();
        var existing = await McpRequestRecord.Get(id, ct);
        if (existing is not null)
        {
            if (!existing.Matches(operation, targetKey, fingerprint)) throw new McpRequestConflictException(requestId);
            return new Registration(existing, true);
        }
        var record = new McpRequestRecord
        {
            Id = id, CredentialId = credentialId, ParticipantId = did, RequestId = requestId,
            Operation = operation, TargetKey = targetKey, Fingerprint = fingerprint,
            NamespacedOperationId = McpRequestRecord.BuildOperationId(credentialId, did, requestId),
            RegisteredAt = clock.GetUtcNow()
        };
        await record.Save(ct);
        return new Registration(record, false);
    }

    public async Task<McpRequestRecord?> Find(string credentialId, string did, string requestId, CancellationToken ct)
    {
        McpRequestRecord.CheckRequestId(requestId);
        using var fresh = EntityContext.NoCache();
        return await McpRequestRecord.Get(McpRequestRecord.Key(credentialId, did, requestId), ct);
    }

    public async Task Complete(McpRequestRecord record, string state, string? resultRef, string? resultData, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var stored = await McpRequestRecord.Get(record.Id, ct) ?? record;
        stored.Complete(state, resultRef, resultData, clock.GetUtcNow());
        // The atomic receipt already proves the mutation. Enrich its presentation after commit,
        // e.g. the newly visible channel directory, without changing the terminal outcome.
        if (stored.State == "completed" && state == "completed" && resultData is not null)
        { stored.ResultData = resultData; stored.ResultRef = resultRef ?? stored.ResultRef; }
        await stored.Save(ct);
    }

    public void CompleteWithDomain(McpRequestRecord record, Func<object?, (string State, string? Reference, string Data)> map)
    {
        var current = binding.Value ?? throw new InvalidOperationException("Receipt binding requires a running command.");
        current.Record = record;
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

public sealed class McpRequestConflictException(string requestId) : Exception
{
    public string RequestId { get; } = requestId;
}

