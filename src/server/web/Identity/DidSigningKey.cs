using System.Numerics;
using System.Security.Cryptography;
using CarpaNet.Identity;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using BcInteger = Org.BouncyCastle.Math.BigInteger;

namespace TangentSpace.Identity;

/// <summary>A DID's current document-controlled atproto signing key. JWT claims and algorithm checks belong to the caller.</summary>
public sealed class DidSigningKey
{
    private readonly ECPublicKeyParameters publicKey;

    private DidSigningKey(string did, string curve, byte[] encodedPoint)
    {
        Did = did;
        Curve = curve;
        var parameters = CustomNamedCurves.GetByName(curve == "P-256" ? "secp256r1" : "secp256k1");
        var domain = new ECDomainParameters(parameters.Curve, parameters.G, parameters.N, parameters.H);
        try
        {
            publicKey = new ECPublicKeyParameters(parameters.Curve.DecodePoint(encodedPoint), domain);
        }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid atproto signing key point.", error); }
    }

    public string Did { get; }
    public string Curve { get; }
    public string JwtAlgorithm => Curve == "P-256" ? "ES256" : "ES256K";

    public static DidSigningKey FromDidDocument(DidDocument document, string expectedDid)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Id != expectedDid || string.IsNullOrWhiteSpace(expectedDid))
            throw new InvalidDataException("The DID document belongs to a different DID.");
        var candidates = document.VerificationMethod?.Where(method => method is not null && (method.Id == "#atproto" || method.Id == expectedDid + "#atproto")).ToArray() ?? [];
        if (candidates.Length != 1 || candidates[0].Controller != expectedDid)
            throw new InvalidDataException("Expected one document-controlled #atproto signing key.");
        var method = candidates[0];
        var encoded = DecodeBase58(method.PublicKeyMultibase);
        if (method.Type is "EcdsaSecp256k1VerificationKey2019" or "EcdsaSecp256r1VerificationKey2019")
        {
            // The 2019 key types carry the raw EC point in their multibase, not a multicodec envelope.
            if (!(encoded.Length == 33 && encoded[0] is 2 or 3) && !(encoded.Length == 65 && encoded[0] == 4))
                throw new InvalidDataException("Expected a compressed or uncompressed 2019 atproto signing key.");
            return new(expectedDid, method.Type == "EcdsaSecp256k1VerificationKey2019" ? "secp256k1" : "P-256", encoded);
        }
        if (method.Type != "Multikey" || encoded.Length != 35)
            throw new InvalidDataException("Expected a supported atproto Multikey envelope.");
        var curve = (encoded[0], encoded[1]) switch
        {
            (0xe7, 0x01) => "secp256k1",
            (0x80, 0x24) => "P-256",
            _ => throw new InvalidDataException("Unsupported atproto signing-key multicodec.")
        };
        if (encoded[2] is not (2 or 3)) throw new InvalidDataException("Expected a compressed atproto signing key.");
        return new(expectedDid, curve, encoded[2..]);
    }

    /// <summary>Verify a compact 64-byte low-S ECDSA signature over SHA-256 of the exact supplied bytes.</summary>
    public bool VerifySignature(ReadOnlySpan<byte> signingInput, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != 64) return false;
        var r = new BcInteger(1, signature[..32].ToArray());
        var s = new BcInteger(1, signature[32..].ToArray());
        var order = publicKey.Parameters.N;
        if (r.SignValue <= 0 || r.CompareTo(order) >= 0 || s.SignValue <= 0 || s.CompareTo(order.ShiftRight(1)) > 0) return false;
        var verifier = new ECDsaSigner();
        verifier.Init(false, publicKey);
        return verifier.VerifySignature(SHA256.HashData(signingInput), r, s);
    }

    // CarpaNet's base58 helpers are private. This bounded conversion handles only atproto key
    // encodings; curve math stays in BouncyCastle.
    private static byte[] DecodeBase58(string? value)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        if (value is null || value.Length is < 2 or > 128 || value[0] != 'z' || value[1] == '1')
            throw new InvalidDataException("Expected a bounded base58btc atproto Multikey.");
        var number = BigInteger.Zero;
        foreach (var character in value.AsSpan(1))
        {
            var digit = alphabet.IndexOf(character);
            if (digit < 0) throw new InvalidDataException("Invalid base58btc atproto Multikey.");
            number = number * 58 + digit;
        }
        var bytes = number.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length is < 33 or > 65) throw new InvalidDataException("atproto signing key exceeds its supported encoding bound.");
        return bytes;
    }
}
