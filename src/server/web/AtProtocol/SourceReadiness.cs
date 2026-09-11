using CarpaNet.Identity;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace TangentSpace.AtProtocol;

/// <summary>Describes whether a participant's durable source grant can perform Tangent Spaces operations.
/// It reports grant shape only; it does not claim that a live source operation is fresh or available.</summary>
public sealed class SourceReadiness(AtprotoSessions sessions, IOptions<SpacesOptions> configured, IConfiguration configuration)
{
    private readonly SpacesOptions options = configured.Value;
    private readonly HashSet<string> unsupportedOrigins = configuration
        .GetSection(SpacesOptions.Configuration + ":UnsupportedProviderOrigins").Get<string[]>()?
        .Select(NormalizeOrigin).Where(x => x is not null).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

    public async Task<SourceReadinessResult> Get(string did, string? room = null, CancellationToken ct = default)
    {
        if (!IdentityResolver.IsValidDid(did)) throw new ArgumentException("A valid participant DID is required.", nameof(did));
        var connectUrl = "/api/connections/rooms" + (room is null ? "" : "?room=" + Uri.EscapeDataString(room));
        try
        {
            var session = await sessions.GetSessionMetadata(did, ct);
            var pds = session?.Pds;
            if (pds is null)
                pds = (await sessions.ResolveDid(did, ct)).PdsEndpoint
                    ?? throw new InvalidOperationException("The participant identity has no source provider.");
            var canRead = session is not null && HasAction(session.Scopes, "read");
            var canWrite = session is not null && HasAction(session.Scopes, "create");
            if (canRead && canWrite) return new(did, true, true, "ready", connectUrl);
            if (IsUnsupported(pds)) return new(did, canRead, canWrite, "provider-unsupported", connectUrl,
                "This account provider is configured as not supporting Tangent source Spaces.");
            return new(did, canRead, canWrite, "consent-required", connectUrl,
                "Connect your account with the required Tangent source access.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new(did, false, false, "disconnected", connectUrl,
                "The saved source connection could not be restored. Connect your account again.");
        }
    }

    private bool HasAction(IEnumerable<string> scopes, string action)
        => scopes.Any(scope => ScopeHasAction(scope, action));

    private bool ScopeHasAction(string scope, string action)
    {
        var prefix = "space:" + SpacesOptions.SpaceType + "?";
        if (!scope.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var values = scope[prefix.Length..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)).Where(pair => pair.Length == 2)
            .GroupBy(pair => Uri.UnescapeDataString(pair[0]), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(pair => Uri.UnescapeDataString(pair[1])).ToArray(), StringComparer.Ordinal);
        return values.TryGetValue("authority", out var authority) && authority.Contains(options.AuthorityDid, StringComparer.Ordinal)
            && values.TryGetValue("collection", out var collection) && collection.Contains(SpacesOptions.Collection, StringComparer.Ordinal)
            && values.TryGetValue("action", out var actions) && actions.Contains(action, StringComparer.Ordinal);
    }

    private bool IsUnsupported(string? pds) => NormalizeOrigin(pds) is { } origin && unsupportedOrigins.Contains(origin);

    private static string? NormalizeOrigin(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo)
            ? uri.GetLeftPart(UriPartial.Authority) : null;
}

public sealed record SourceReadinessResult(string Did, bool CanReadSource, bool CanWriteSource, string State,
    string ConnectUrl, string? Reason = null);
