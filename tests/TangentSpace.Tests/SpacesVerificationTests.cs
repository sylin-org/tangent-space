using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarpaNet.Identity;
using Org.BouncyCastle.Crypto.EC;
using TangentSpace.AtProtocol.Verification;
using Xunit;

namespace TangentSpace.Tests;

public sealed class SpacesVerificationTests
{
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "VerificationFixtures");

    public static IEnumerable<object[]> OfficialVectors()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "manifest.json")));
        foreach (var vector in document.RootElement.GetProperty("tests").EnumerateArray())
            yield return [vector.GetRawText()];
    }

    [Theory, MemberData(nameof(OfficialVectors))]
    public void Official_CAR_oracle_outcomes_match(string json)
    {
        using var parsed = JsonDocument.Parse(json);
        var vector = parsed.RootElement;
        var author = vector.GetProperty("author").GetString()!;
        var key = Key(author, vector.GetProperty("didKey").GetString()!["did:key:".Length..]);
        var car = File.ReadAllBytes(Path.Combine(Fixtures, vector.GetProperty("file").GetString()!));
        var error = Record.Exception(() => SpaceCarVerifier.Verify(car, vector.GetProperty("space").GetString()!, author, key, vector.GetProperty("expectValues").GetBoolean()));
        if (vector.GetProperty("expected").GetBoolean()) Assert.Null(error);
        else Assert.NotNull(error);
    }

    [Fact]
    public void Official_context_and_all_LtHash_lanes_match()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "manifest.json")));
        var vector = document.RootElement.GetProperty("primitiveVectors");
        var state = SpaceCarVerifier.ComputeSetState(vector.GetProperty("elements").EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal(vector.GetProperty("stateHex").GetString(), Convert.ToHexStringLower(state));
        Assert.Equal(vector.GetProperty("digestHex").GetString(), Convert.ToHexStringLower(SHA256.HashData(state)));
        var context = vector.GetProperty("ctx");
        var encoded = SpaceCarVerifier.EncodeContext(context.GetProperty("space").GetString()!, context.GetProperty("author").GetString()!,
            context.GetProperty("rev").GetString()!, Convert.FromHexString(vector.GetProperty("ikmHex").GetString()!));
        Assert.Equal(vector.GetProperty("encodedContextHex").GetString(), Convert.ToHexStringLower(encoded));
    }

    [Theory, InlineData(0), InlineData(1)]
    public void Duplicate_values_require_one_block_per_indexed_path(int number)
    {
        using var parsed = JsonDocument.Parse(AdditionalVectors);
        var vector = parsed.RootElement[number];
        var author = vector.GetProperty("author").GetString()!;
        var key = Key(author, vector.GetProperty("multikey").GetString()!);
        var car = Convert.FromBase64String(vector.GetProperty("carBase64").GetString()!);
        var result = SpaceCarVerifier.Verify(car, vector.GetProperty("space").GetString()!, author, key);
        Assert.Equal(2, result.Records.Count);
        Assert.Equal(result.Records[0].Cid, result.Records[1].Cid);
        Assert.NotEqual(result.Records[0].RecordKey, result.Records[1].RecordKey);
        var deduplicated = car[..^vector.GetProperty("finalFrameLength").GetInt32()];
        Assert.Throws<InvalidDataException>(() => SpaceCarVerifier.Verify(deduplicated, result.Space, author, key));
    }

    [Theory, InlineData(0), InlineData(1)]
    public void Current_multikey_verifies_official_compact_signature_and_rejects_tampering(int number)
    {
        using var parsed = JsonDocument.Parse(AdditionalVectors);
        var vector = parsed.RootElement[number];
        var author = vector.GetProperty("author").GetString()!;
        var key = Key(author, vector.GetProperty("multikey").GetString()!);
        Assert.Equal(vector.GetProperty("curve").GetString(), key.Curve);
        Assert.Equal(key.Curve == "P-256" ? "ES256" : "ES256K", key.JwtAlgorithm);
        var input = Encoding.UTF8.GetBytes(vector.GetProperty("signingInput").GetString()!);
        var signature = Convert.FromHexString(vector.GetProperty("signatureHex").GetString()!);
        Assert.True(key.VerifySignature(input, signature));
        Assert.False(key.VerifySignature(Encoding.UTF8.GetBytes("changed context"), signature));
        Assert.False(key.VerifySignature(input, signature[..63]));
        var altered = (byte[])signature.Clone(); altered[0] ^= 1;
        Assert.False(key.VerifySignature(input, altered));
        var order = CustomNamedCurves.GetByName(key.Curve == "P-256" ? "secp256r1" : "secp256k1").N.ToByteArrayUnsigned();
        var highS = new BigInteger(order, true, true) - new BigInteger(signature.AsSpan(32), true, true);
        var highBytes = highS.ToByteArray(true, true);
        altered = (byte[])signature.Clone(); Array.Clear(altered, 32, 32); highBytes.CopyTo(altered, 64 - highBytes.Length);
        Assert.False(key.VerifySignature(input, altered));
    }

    [Theory]
    [InlineData("other-document")][InlineData("other-controller")][InlineData("other-key-id")]
    [InlineData("missing-key")][InlineData("duplicate-key")][InlineData("legacy-key")]
    [InlineData("wrong-base")][InlineData("invalid-base58")][InlineData("leading-zero")][InlineData("too-long")]
    public void Hostile_or_ambiguous_DID_keys_are_rejected(string change)
    {
        using var parsed = JsonDocument.Parse(AdditionalVectors);
        var vector = parsed.RootElement[0];
        var author = vector.GetProperty("author").GetString()!;
        var document = Document(author, vector.GetProperty("multikey").GetString()!);
        var key = document.VerificationMethod[0];
        switch (change)
        {
            case "other-document": document.Id = "did:example:other"; break;
            case "other-controller": key.Controller = "did:example:other"; break;
            case "other-key-id": key.Id = "did:example:other#atproto"; break;
            case "missing-key": document.VerificationMethod.Clear(); break;
            case "duplicate-key": document.VerificationMethod.Add(new VerificationMethod { Id = "#atproto", Controller = author, Type = key.Type, PublicKeyMultibase = key.PublicKeyMultibase }); break;
            case "legacy-key": key.Type = "EcdsaSecp256k1VerificationKey2019"; break;
            case "wrong-base": key.PublicKeyMultibase = "m" + key.PublicKeyMultibase![1..]; break;
            case "invalid-base58": key.PublicKeyMultibase = "z0" + key.PublicKeyMultibase![2..]; break;
            case "leading-zero": key.PublicKeyMultibase = "z1" + key.PublicKeyMultibase![1..]; break;
            case "too-long": key.PublicKeyMultibase = "z" + new string('2', 128); break;
        }
        Assert.Throws<InvalidDataException>(() => ResolvedAuthorKey.FromDidDocument(document, author));
    }

    [Fact]
    public void Relative_atproto_key_id_is_controlled_by_the_document()
    {
        using var parsed = JsonDocument.Parse(AdditionalVectors);
        var vector = parsed.RootElement[0];
        var author = vector.GetProperty("author").GetString()!;
        var document = Document(author, vector.GetProperty("multikey").GetString()!);
        document.VerificationMethod[0].Id = "#atproto";
        Assert.Equal(author, ResolvedAuthorKey.FromDidDocument(document, author).SubjectDid);
    }

    private static ResolvedAuthorKey Key(string author, string multikey) => ResolvedAuthorKey.FromDidDocument(Document(author, multikey), author);
    [Fact]
    public void Actual_legacy_PLC_documents_verify_real_service_signature_and_PDS_CAR()
    {
        using var fixtures = JsonDocument.Parse(LegacyFixtures);
        var authority = DidDocument.FromJson(fixtures.RootElement.GetProperty("authorityDocument").GetRawText());
        var authorityKey = ResolvedAuthorKey.FromDidDocument(authority, authority.Id);
        var signature = fixtures.RootElement.GetProperty("expiredServiceSignature");
        Assert.True(authorityKey.VerifySignature(Encoding.ASCII.GetBytes(signature.GetProperty("signingInput").GetString()!),
            Convert.FromHexString(signature.GetProperty("signatureHex").GetString()!)));
        var agent = DidDocument.FromJson(fixtures.RootElement.GetProperty("agentDocument").GetRawText());
        var key = ResolvedAuthorKey.FromDidDocument(agent, agent.Id);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "manifest.json")));
        var actual = manifest.RootElement.GetProperty("tests").EnumerateArray().Single(item => item.GetProperty("name").GetString() == "actual-pds-repository");
        Assert.Equal(actual.GetProperty("author").GetString(), key.SubjectDid);
        var result = SpaceCarVerifier.Verify(File.ReadAllBytes(Path.Combine(Fixtures, "actual-pds-repository.car")),
            actual.GetProperty("space").GetString()!, key.SubjectDid, key);
        Assert.Equal(3, result.Records.Count);
    }

    // Public PLC documents and an expired real service signature captured from S01.
    // This tests key/signature decoding only; an expired JWT is not valid service authorization.
    private const string LegacyFixtures = """
{"upstreamRevision":"c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae","authorityDocument":{"@context":["https://www.w3.org/ns/did/v1","https://w3id.org/security/suites/secp256k1-2019/v1"],"id":"did:plc:yblwluenzocgiqpmu4ip7six","alsoKnownAs":["at://tangent-authority.test"],"verificationMethod":[{"id":"#atproto","type":"EcdsaSecp256k1VerificationKey2019","controller":"did:plc:yblwluenzocgiqpmu4ip7six","publicKeyMultibase":"zNG8hhDBG9gTy1aMrHBVXQNoVWodeW8w56jfXt9GvmutDGB2LkskwtYpRhd5jE2KrLQnZJJePqY8cK1ncCi5cRkLQ"}],"service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"http://localhost:2583"}]},"agentDocument":{"@context":["https://www.w3.org/ns/did/v1","https://w3id.org/security/suites/secp256k1-2019/v1"],"id":"did:plc:mgxxqowf6nnd3btckqjq573l","alsoKnownAs":["at://tangent-agent.test2"],"verificationMethod":[{"id":"#atproto","type":"EcdsaSecp256k1VerificationKey2019","controller":"did:plc:mgxxqowf6nnd3btckqjq573l","publicKeyMultibase":"zSQF3qWbckpLdEEFjteSKLZMkaZTJCiQKVQb9EJ7gevMCts2ddDieujDXAps4Nsh7H5MdC8HAdQhXpSKphFXeQbVK"}],"service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"http://localhost:2584"}]},"expiredServiceSignature":{"expiresAt":1788988298,"signingInput":"eyJ0eXAiOiJKV1QiLCJhbGciOiJFUzI1NksifQ.eyJpYXQiOjE3ODg5ODgyOTUsImlzcyI6ImRpZDpwbGM6eWJsd2x1ZW56b2NnaXFwbXU0aXA3c2l4IiwiYXVkIjoiZGlkOnBsYzpzYTJmbDJ0MjduN202NHBibDR4YXpkc20jdGFuZ2VudCIsImV4cCI6MTc4ODk4ODI5OCwibHhtIjoiY29tLmF0cHJvdG8uc2ltcGxlc3BhY2UuY2hlY2tVc2VyQWNjZXNzIiwianRpIjoiODBmMDFkOWIzYWFjMzJhYjFmNjg0ZmZjMDEwN2ZlNWEifQ","signatureHex":"752c6c4b067c1639d204a7e9f4af83f2819fbd3d1c3b59c4642b2ef39759d6ce02e50c015ba25073ccf445015587cee4322d2cf30c925abd7f7eb4481dec2624"}}
""";

    private static DidDocument Document(string author, string multikey) => new()
    {
        Id = author,
        VerificationMethod = [new VerificationMethod { Id = author + "#atproto", Controller = author, Type = "Multikey", PublicKeyMultibase = multikey }]
    };

    // These test format integrity, not transferable record authorship: the upstream
    // signature covers context, while a recipient can recompute the digest's MAC.
    // Production must obtain the CAR from an authenticated expected author/PDS.
    // Public vectors generated by the pinned official serializer/verifier and keypair implementations.
    // Reproduce with AtProtocol/Verification/generate-integration-vectors.mjs in the S01 image.
    private const string AdditionalVectors = """
[{"curve":"secp256k1","space":"at://did:example:site/space/local.tangent.room/duplicates","author":"did:example:author","rev":"3kbcq3p7ad400","multikey":"zQ3shuCPf2Fkw8aWDT41Tbq4mXi73WcpkEsgjZCpbWUgtG3kh","carBase64":"Y6Jlcm9vdHOC2CpYJQABcRIgQuqSYKbjZWzpnNDqbXzz1MFsvi6bdkpLZrvfJ5f+/4jYKlglAAFxEiAxjrWuRQceOKOef8XmjwrhXy+zDidiuXewl3F0Aa9M62d2ZXJzaW9uAfUBAXESIELqkmCm42Vs6ZzQ6m1889TBbL4um3ZKS2a73yeX/v+IpmNpa21YIFa5AxJUs4qWmVVB0PCweVedZZ05LpjiATu6Nt8EpY+5Y21hY1ggsGH4tG0BBJ7IVj7lRUULaPKQ1KHOdD8BsDpFxJi2CF1jcmV2bTNrYmNxM3A3YWQ0MDBjc2lnWEB1qXA+deBfAs9HHrYlljx7R4lU5pX2WwWOHJ+PL1apP0BrYjWPBrXub2VE82gHpLs5ILhTG9e6T3fo9X8hsd6eY3ZlcgFkaGFzaFggX4UcIF3foskhrrxREwtYqWnY7QI0rBcakr2mP0ymlpmtAQFxEiAxjrWuRQceOKOef8XmjwrhXy+zDidiuXewl3F0Aa9M66J4GWxvY2FsLnRhbmdlbnQubWVzc2FnZS9vbmXYKlglAAFxEiCcNjAQyORdMhO2EHpUNC92fUrKY1WznTDeObRMSKI8c3gZbG9jYWwudGFuZ2VudC5tZXNzYWdlL3R3b9gqWCUAAXESIJw2MBDI5F0yE7YQelQ0L3Z9SspjVbOdMN45tExIojxzUQFxEiCcNjAQyORdMhO2EHpUNC92fUrKY1WznTDeObRMSKI8c6JkdGV4dGpzYW1lIHZhbHVlZSR0eXBldWxvY2FsLnRhbmdlbnQubWVzc2FnZVEBcRIgnDYwEMjkXTITthB6VDQvdn1KymNVs50w3jm0TEiiPHOiZHRleHRqc2FtZSB2YWx1ZWUkdHlwZXVsb2NhbC50YW5nZW50Lm1lc3NhZ2U=","finalFrameLength":82,"deduplicatedRejected":true,"signingInput":"eyJhbGciOiJ0ZXN0In0.eyJpc3MiOiJkaWQ6ZXhhbXBsZTphdXRob3IifQ","signatureHex":"3ca74af623d46ac2cf80d0dfcea13e64b91db4c67ec2fb1ba01e9422caa5fe06516d5a285700616f76bea1d1ced98b508ec505f5ef177c2176d2e3c7b9308190"},{"curve":"P-256","space":"at://did:example:site/space/local.tangent.room/duplicates","author":"did:example:author","rev":"3kbcq3p7ad400","multikey":"zDnaejR8NosRdn9e77oK5LSu1akYxRgNA9aS6bNvsziVWownc","carBase64":"Y6Jlcm9vdHOC2CpYJQABcRIgInkIfIAmTtinpuOFEgbPVbo+OtSxLEICSuC/JVv1NdfYKlglAAFxEiAxjrWuRQceOKOef8XmjwrhXy+zDidiuXewl3F0Aa9M62d2ZXJzaW9uAfUBAXESICJ5CHyAJk7Yp6bjhRIGz1W6PjrUsSxCAkrgvyVb9TXXpmNpa21YIFmPBVrkSNzQcqu+py4WUJSStDvM7C0yxdjNyJO4h4nsY21hY1ggPODjkCIOoiJaS4RlWJLiPoAjXphbuuwiSNMR1DrV7ctjcmV2bTNrYmNxM3A3YWQ0MDBjc2lnWECkM0s1j7Eg3FORpu7b9NAiAnmgL3v9dEOKX4rSEKmJ30ommCwdl5iphd7/sB8aCR2PNA9Z6eGy6HpkNQ4qiIOCY3ZlcgFkaGFzaFggX4UcIF3foskhrrxREwtYqWnY7QI0rBcakr2mP0ymlpmtAQFxEiAxjrWuRQceOKOef8XmjwrhXy+zDidiuXewl3F0Aa9M66J4GWxvY2FsLnRhbmdlbnQubWVzc2FnZS9vbmXYKlglAAFxEiCcNjAQyORdMhO2EHpUNC92fUrKY1WznTDeObRMSKI8c3gZbG9jYWwudGFuZ2VudC5tZXNzYWdlL3R3b9gqWCUAAXESIJw2MBDI5F0yE7YQelQ0L3Z9SspjVbOdMN45tExIojxzUQFxEiCcNjAQyORdMhO2EHpUNC92fUrKY1WznTDeObRMSKI8c6JkdGV4dGpzYW1lIHZhbHVlZSR0eXBldWxvY2FsLnRhbmdlbnQubWVzc2FnZVEBcRIgnDYwEMjkXTITthB6VDQvdn1KymNVs50w3jm0TEiiPHOiZHRleHRqc2FtZSB2YWx1ZWUkdHlwZXVsb2NhbC50YW5nZW50Lm1lc3NhZ2U=","finalFrameLength":82,"deduplicatedRejected":true,"signingInput":"eyJhbGciOiJ0ZXN0In0.eyJpc3MiOiJkaWQ6ZXhhbXBsZTphdXRob3IifQ","signatureHex":"bf1bb04425f5396aadf618fe6ae414a66a0368e8a1cc9978c0ebde5348e47c1630e5a5045436e968b8843411562a03d958c3be22acc8c72f7bd0b3cfe04e5304"}]
""";
}
