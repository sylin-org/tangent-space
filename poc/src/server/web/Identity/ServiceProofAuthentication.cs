using System.Text;
using System.Text.Json;
using System.Net.Sockets;
using CarpaNet.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Tangent.Activity;

namespace Tangent.Identity;

/// <summary>Verifies a participant-issued atproto service-auth JWT addressed to this server's enrollment proof
/// exchange, authenticating the token's own issuer DID against its current signing key.</summary>
public sealed class ServiceProofAuthentication(IServiceProofKeySource keys, ProofAudience proofAudience,
    TimeProvider clock, ILogger<ServiceProofAuthentication> logger)
{
    private const string BearerPrefix = "Bearer ";

    public async Task<ServiceProofResult> Verify(string authorization, CancellationToken ct)
    {
        if (proofAudience.Value is not { } audience) return new(ServiceProofStatus.Unconfigured);
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
                || !h.TryGetProperty("typ", out var type) || type.GetString() != "JWT"
                || (h.TryGetProperty("kid", out var kid) && kid.GetString() != "#atproto")
                || h.GetProperty("alg").GetString() is not ("ES256" or "ES256K"))
                return new(ServiceProofStatus.Invalid);
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            var issuer = p.GetProperty("iss").GetString();
            if (issuer is null || !IdentityResolver.IsValidDid(issuer)
                || p.GetProperty("aud").GetString() != audience
                || p.GetProperty("lxm").GetString() != ServiceAuthenticationConstants.ExchangeMethod
                || p.GetProperty("exp").GetInt64() <= now || p.GetProperty("exp").GetInt64() > now + 300
                || p.GetProperty("iat").GetInt64() < now - 300 || p.GetProperty("iat").GetInt64() > now + 30
                || p.GetProperty("exp").GetInt64() <= p.GetProperty("iat").GetInt64()
                || p.GetProperty("exp").GetInt64() - p.GetProperty("iat").GetInt64() > 300
                || (p.TryGetProperty("nbf", out var before) && before.GetInt64() > now)
                || p.GetProperty("jti").GetString() is not { Length: > 0 and <= 128 } jti
                || jti.Any(char.IsControl))
            {
                logger.LogDebug("Service proof rejected before key resolution.");
                return new(ServiceProofStatus.Invalid);
            }
            DidSigningKey key;
            try { key = await keys.Resolve(issuer, ct); }
            catch (Exception error) when (error is HttpRequestException or SocketException or TimeoutException)
            {
                logger.LogDebug("Service proof issuer resolution failed: {FailureType}", error.GetType().Name);
                return new(ServiceProofStatus.Unreachable);
            }
            var verified = h.GetProperty("alg").GetString() == key.JwtAlgorithm
                && key.VerifySignature(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), WebEncoders.Base64UrlDecode(parts[2]));
            if (!verified)
            {
                logger.LogDebug("Service proof signing-key verification failed.");
                return new(ServiceProofStatus.Invalid);
            }
            // Async key resolution can straddle the short expiry window; recheck against the current clock.
            if (p.GetProperty("exp").GetInt64() <= clock.GetUtcNow().ToUnixTimeSeconds())
            {
                logger.LogDebug("Service proof expired during verification.");
                return new(ServiceProofStatus.Invalid);
            }
            return new(ServiceProofStatus.Valid, new ServiceProof(issuer, jti, DateTimeOffset.FromUnixTimeSeconds(p.GetProperty("exp").GetInt64())));
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException
            or KeyNotFoundException or InvalidDataException or ArgumentException or OverflowException)
        {
            logger.LogDebug("Service proof validation failed: {FailureType}", error.GetType().Name);
            return new(ServiceProofStatus.Invalid);
        }
    }
}
