using System.Text;
using System.Text.Json;
using System.Net.Sockets;
using CarpaNet.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TangentSpace.AtProtocol;
using TangentSpace.AtProtocol.Verification;

namespace TangentSpace.Mcp.Authentication;

/// <summary>Verifies a participant-issued AT service-auth JWT addressed to this Tangent's MCP proof exchange.
/// Mirrors the authority-pinned callback verifier's hygiene while authenticating the token's own issuer DID.</summary>
public sealed class ServiceProofAuthentication(IServiceProofKeySource keys, IOptions<SpacesOptions> configured,
    TimeProvider clock, ILogger<ServiceProofAuthentication> logger)
{
    private const string BearerPrefix = "Bearer ";

    public async Task<ServiceProofResult> Verify(string authorization, CancellationToken ct)
    {
        var audience = configured.Value.ManagingApp;
        if (string.IsNullOrWhiteSpace(audience)) return new(ServiceProofStatus.Unconfigured);
        if (!authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase) || authorization.Length > 8192)
            return new(ServiceProofStatus.Invalid);
        try
        {
            var parts = authorization[BearerPrefix.Length..].Split('.');
            if (parts.Length != 3) return new(ServiceProofStatus.Invalid);
            using var header = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[0]));
            using var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));
            var h = header.RootElement;
            var p = payload.RootElement;
            if (h.EnumerateObject().Select(x => x.Name).Distinct().Count() != h.EnumerateObject().Count()
                || p.EnumerateObject().Select(x => x.Name).Distinct().Count() != p.EnumerateObject().Count()
                || h.TryGetProperty("crit", out _)
                || (h.TryGetProperty("typ", out var type) && type.GetString() != "JWT")
                || h.GetProperty("alg").GetString() is not ("ES256" or "ES256K"))
                return new(ServiceProofStatus.Invalid);
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            var issuer = p.GetProperty("iss").GetString();
            if (issuer is null || !IdentityResolver.IsValidDid(issuer)
                || p.GetProperty("aud").GetString() != audience
                || p.GetProperty("lxm").GetString() != McpAuthenticationConstants.ExchangeMethod
                || p.GetProperty("exp").GetInt64() <= now || p.GetProperty("exp").GetInt64() > now + 300
                || p.GetProperty("iat").GetInt64() < now - 300 || p.GetProperty("iat").GetInt64() > now + 30
                || (p.TryGetProperty("nbf", out var before) && before.GetInt64() > now)
                || (p.TryGetProperty("sub", out var subject) && subject.GetString() != issuer)
                || p.GetProperty("jti").GetString() is not { Length: > 0 and <= 128 } jti
                || jti.Any(char.IsControl))
            {
                logger.LogDebug("MCP service proof rejected before key resolution.");
                return new(ServiceProofStatus.Invalid);
            }
            ResolvedAuthorKey key;
            try { key = await keys.Resolve(issuer, ct); }
            catch (Exception error) when (error is HttpRequestException or SocketException or TimeoutException)
            {
                logger.LogDebug("MCP service proof issuer resolution failed: {FailureType}", error.GetType().Name);
                return new(ServiceProofStatus.Unreachable);
            }
            var verified = h.GetProperty("alg").GetString() == key.JwtAlgorithm
                && key.VerifySignature(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), WebEncoders.Base64UrlDecode(parts[2]));
            if (!verified)
            {
                logger.LogDebug("MCP service proof signing-key verification failed.");
                return new(ServiceProofStatus.Invalid);
            }
            // Async key resolution can straddle the short expiry window; recheck against the current clock.
            if (p.GetProperty("exp").GetInt64() <= clock.GetUtcNow().ToUnixTimeSeconds())
            {
                logger.LogDebug("MCP service proof expired during verification.");
                return new(ServiceProofStatus.Invalid);
            }
            return new(ServiceProofStatus.Valid, new ServiceProof(issuer, jti, DateTimeOffset.FromUnixTimeSeconds(p.GetProperty("exp").GetInt64())));
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException
            or KeyNotFoundException or InvalidDataException or ArgumentException or OverflowException)
        {
            logger.LogDebug("MCP service proof validation failed: {FailureType}", error.GetType().Name);
            return new(ServiceProofStatus.Invalid);
        }
    }
}
