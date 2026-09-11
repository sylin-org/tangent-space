using Koan.Web.Auth.Connector.Atproto.Protocol;

namespace TangentSpace.AtProtocol.Verification;

/// <summary>Verifies a complete pinned-format Spaces CAR from an authenticated expected-PDS response, using its author's current DID key.</summary>
public sealed class SpacesVerifier(AtprotoSessions sessions)
{
    public async Task<ResolvedAuthorKey> ResolveAuthorKey(string authorDid, CancellationToken ct = default)
        => ResolvedAuthorKey.FromDidDocument(await sessions.ResolveDid(authorDid, ct), authorDid);

    public async Task<VerifiedSpaceRepo> Verify(byte[] car, string expectedSpace, string expectedAuthor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(car);
        if (car.Length is 0 or > SpaceCarVerifier.MaxCarBytes) throw new InvalidDataException("CAR size outside supported bound.");
        var key = await ResolveAuthorKey(expectedAuthor, ct);
        ct.ThrowIfCancellationRequested();
        var verified = SpaceCarVerifier.Verify(car, expectedSpace, expectedAuthor, key);
        ct.ThrowIfCancellationRequested();
        return verified;
    }
}
