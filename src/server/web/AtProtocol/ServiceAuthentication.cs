using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TangentSpace.AtProtocol.Verification;

namespace TangentSpace.AtProtocol;

public sealed class ServiceAuthentication(SpacesVerifier verifier, IOptions<SpacesOptions> configured, TimeProvider clock, ILogger<ServiceAuthentication> logger)
{
    public async Task<bool> Verify(string authorization, CancellationToken ct)
        => await Verify(authorization, SpacesOptions.AccessMethod, ct);

    public async Task<bool> Verify(string authorization, string expectedMethod, CancellationToken ct)
    {
        if (expectedMethod is not (SpacesOptions.AccessMethod or SpacesOptions.NotifyMethod)) return false;
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || authorization.Length > 8192) return false;
        try
        {
            var parts = authorization[7..].Split('.');
            if (parts.Length != 3) return false;
            using var header = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[0]));
            using var payload = JsonDocument.Parse(WebEncoders.Base64UrlDecode(parts[1]));
            var h = header.RootElement;
            var p = payload.RootElement;
            if (h.EnumerateObject().Select(x => x.Name).Distinct().Count() != h.EnumerateObject().Count()
                || p.EnumerateObject().Select(x => x.Name).Distinct().Count() != p.EnumerateObject().Count()
                || h.TryGetProperty("crit", out _)
                || (h.TryGetProperty("typ", out var type) && type.GetString() != "JWT")) return false;
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            if (p.GetProperty("iss").GetString() != configured.Value.AuthorityDid
                || p.GetProperty("aud").GetString() != configured.Value.ManagingApp
                || p.GetProperty("lxm").GetString() != expectedMethod
                || p.GetProperty("exp").GetInt64() <= now || p.GetProperty("exp").GetInt64() > now + 300
                || (p.TryGetProperty("iat", out var issued) && issued.GetInt64() > now + 30)
                || (p.TryGetProperty("nbf", out var before) && before.GetInt64() > now))
            {
                logger.LogDebug("Service JWT rejected: authority={Authority}, audience={Audience}, method={Method}, remainingSeconds={Remaining}",
                    p.GetProperty("iss").GetString() == configured.Value.AuthorityDid, p.GetProperty("aud").GetString() == configured.Value.ManagingApp,
                    p.GetProperty("lxm").GetString() == expectedMethod, p.GetProperty("exp").GetInt64() - now);
                return false;
            }
            var key = await verifier.ResolveAuthorKey(configured.Value.AuthorityDid, ct);
            var verified = h.GetProperty("alg").GetString() == key.JwtAlgorithm
                && key.VerifySignature(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), WebEncoders.Base64UrlDecode(parts[2]));
            if (!verified) logger.LogDebug("Service JWT signing-key verification failed.");
            return verified;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or InvalidDataException or ArgumentException)
        { logger.LogDebug("Service JWT validation failed: {FailureType}", error.GetType().Name); return false; }
    }
}
