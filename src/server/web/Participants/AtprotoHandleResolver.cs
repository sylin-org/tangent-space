using CarpaNet.Identity;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Microsoft.Extensions.Caching.Memory;

namespace TangentSpace.Participants;

/// <summary>The single seam over DID-document handle resolution, so the label seam stays
/// testable against a fake source. The real source resolves the account's DID document
/// through the same guarded transport proof verification already uses and reports the
/// atproto handle the document declares (alsoKnownAs at:// entries).</summary>
public interface IAtprotoHandleSource
{
    Task<string?> HandleOf(string did, CancellationToken ct);
}

/// <summary>Read-time handle resolution (12 September 2026 decision): an atproto identity
/// whose stored label is null has its handle resolved from the current DID document.
/// Answers — including failures — are cached so repeated byline and mentionable renders
/// never re-fetch; an unreachable or handle-less DID yields null and the caller falls back
/// to honest internal display. Resolution is bounded and never writes.</summary>
internal sealed class AtprotoHandleResolver(AtprotoSessions sessions, IMemoryCache cache,
    ILogger<AtprotoHandleResolver> logger) : IAtprotoHandleSource
{
    private sealed record Resolution(string? Handle, bool FromDocument);
    private readonly SemaphoreSlim requests = new(6, 6);

    public async Task<string?> HandleOf(string did, CancellationToken ct)
    {
        if (cache.TryGetValue<Resolution>("tangent-handle:" + did, out var saved)) return saved!.Handle;
        Resolution outcome = new(null, false);
        var entered = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await requests.WaitAsync(timeout.Token);
            entered = true;
            if (cache.TryGetValue<Resolution>("tangent-handle:" + did, out saved)) return saved!.Handle;
            outcome = Document(await sessions.ResolveDid(did, timeout.Token));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // A display label must never block or fail the read that asked for it.
            logger.LogDebug("DID handle resolution failed for {Did}: {FailureType}", did, error.GetType().Name);
        }
        finally { if (entered) requests.Release(); }
        // A document-backed answer holds for the label cache window; a failure retries sooner
        // so a transient outage does not pin the honest fallback for minutes.
        cache.Set("tangent-handle:" + did, outcome, outcome.FromDocument ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30));
        return outcome.Handle;
    }

    /// <summary>The document-declared handle, bounded to the display shape bylines consume.</summary>
    private static Resolution Document(DidDocument document)
        => document.Handle is { Length: > 0 and <= 253 } declared && !declared.Any(char.IsWhiteSpace)
            ? new(declared, true) : new(null, true);
}
