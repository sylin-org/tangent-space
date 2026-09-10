using System.Numerics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarpaNet.Identity;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Testing.Integration;
using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using TangentSpace.AtProtocol;
using TangentSpace.AtProtocol.Verification;
using TangentSpace.Mcp.Authentication;
using TangentSpace.Participants;
using TangentSpace.Participation;
using Xunit;

namespace TangentSpace.Tests;

public sealed class McpAuthenticationTests
{
    private const string ManagingApp = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb#tangent";
    private const string Authority = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const long FixedNow = 1_800_000_000;

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Generates real signing keys and DID documents per issuer DID. Key resolution is faked; signature verification never is.</summary>
    private sealed class ProofKeys
    {
        private readonly Dictionary<string, Entry> byDid = new(StringComparer.Ordinal);

        private sealed record Entry(ECDomainParameters Domain, ECPrivateKeyParameters Private, DidDocument Document, string Algorithm);

        public string Token(string issuerDid, string? headerJson = null, string? payloadJson = null, string? curve = null)
        {
            var entry = EntryFor(issuerDid, curve);
            var header = headerJson ?? $$"""{"typ":"JWT","alg":"{{entry.Algorithm}}"}""";
            var payload = payloadJson ?? DefaultPayload(issuerDid);
            var encoded = Base64Url(Encoding.ASCII.GetBytes(header)) + "." + Base64Url(Encoding.ASCII.GetBytes(payload));
            var signer = new Org.BouncyCastle.Crypto.Signers.ECDsaSigner();
            signer.Init(true, entry.Private);
            var parts = signer.GenerateSignature(SHA256.HashData(Encoding.ASCII.GetBytes(encoded)));
            var s = parts[1].CompareTo(entry.Domain.N.ShiftRight(1)) > 0 ? entry.Domain.N.Subtract(parts[1]) : parts[1];
            var signature = new byte[64];
            FixedWidth(parts[0].ToByteArrayUnsigned(), signature, 0);
            FixedWidth(s.ToByteArrayUnsigned(), signature, 32);
            return encoded + "." + Base64Url(signature);
        }

        public string DefaultPayload(string issuerDid, string? audience = ManagingApp, string? method = null,
            long? expiresAt = null, long? issuedAt = null, string? jti = null)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["iss"] = issuerDid,
                ["aud"] = audience!,
                ["lxm"] = method ?? McpAuthenticationConstants.ExchangeMethod,
                ["exp"] = expiresAt ?? now + 120,
                ["iat"] = issuedAt ?? now - 5,
                ["jti"] = jti ?? Guid.NewGuid().ToString("n")
            });
        }

        public ResolvedAuthorKey Resolved(string issuerDid) => ResolvedAuthorKey.FromDidDocument(EntryFor(issuerDid, null).Document, issuerDid);

        private Entry EntryFor(string issuerDid, string? curve)
        {
            if (byDid.TryGetValue(issuerDid, out var existing)) return existing;
            var name = curve == "P-256" ? "secp256r1" : "secp256k1";
            var parameters = CustomNamedCurves.GetByName(name);
            var domain = new ECDomainParameters(parameters.Curve, parameters.G, parameters.N, parameters.H);
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(domain, new SecureRandom()));
            var pair = generator.GenerateKeyPair();
            var point = ((ECPublicKeyParameters)pair.Public).Q.GetEncoded(compressed: true);
            var envelope = new byte[35];
            if (name == "secp256k1") { envelope[0] = 0xe7; envelope[1] = 0x01; }
            else { envelope[0] = 0x80; envelope[1] = 0x24; }
            point.CopyTo(envelope, 2);
            var document = new DidDocument
            {
                Id = issuerDid,
                VerificationMethod = [new VerificationMethod { Id = issuerDid + "#atproto", Controller = issuerDid,
                    Type = "Multikey", PublicKeyMultibase = "z" + Base58(envelope) }]
            };
            var entry = new Entry(domain, (ECPrivateKeyParameters)pair.Private, document, name == "secp256k1" ? "ES256K" : "ES256");
            byDid[issuerDid] = entry;
            return entry;
        }
    }

    private sealed class FakeKeySource(ProofKeys keys) : IServiceProofKeySource
    {
        public Task<ResolvedAuthorKey> Resolve(string issuerDid, CancellationToken ct) => Task.FromResult(keys.Resolved(issuerDid));
    }

    private sealed class ThrowingKeySource : IServiceProofKeySource
    {
        public Task<ResolvedAuthorKey> Resolve(string issuerDid, CancellationToken ct) => throw new HttpRequestException("resolver unreachable");
    }

    private static string Base64Url(byte[] value) => WebEncoders.Base64UrlEncode(value);

    private static void FixedWidth(byte[] source, byte[] target, int offset)
    {
        for (var i = 0; i < source.Length; i++) target[offset + 32 - source.Length + i] = source[i];
    }

    private static string Base58(byte[] value)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var digits = new List<int> { 0 };
        foreach (var byte0 in value)
        {
            int carry = byte0;
            for (var i = 0; i < digits.Count; i++) { carry += digits[i] << 8; digits[i] = carry % 58; carry /= 58; }
            while (carry > 0) { digits.Add(carry % 58); carry /= 58; }
        }
        var text = new StringBuilder();
        for (var i = 0; i < value.Length && value[i] == 0; i++) text.Append('1');
        for (var i = digits.Count - 1; i >= 0; i--) text.Append(alphabet[digits[i]]);
        return text.ToString();
    }

    private static ServiceProofAuthentication Verifier(ProofKeys keys, string managingApp = ManagingApp, long? now = null)
        => new(new FakeKeySource(keys), Options.Create(new SpacesOptions { AuthorityDid = Authority, ManagingApp = managingApp }),
            new FixedTime(DateTimeOffset.FromUnixTimeSeconds(now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds())),
            NullLogger<ServiceProofAuthentication>.Instance);

    private static string Bearer(ProofKeys keys, string did, string? curve = null) => "Bearer " + keys.Token(did, curve: curve);

    [Theory, InlineData("did:plc:mcptestsecpaaaaaaaaaaaaa", "secp256k1"), InlineData("did:plc:mcptestpnnnnnnnnnnnnnnnn", "P-256")]
    public async Task Valid_proof_for_both_curves_authenticates_the_issuer(string did, string curve)
    {
        var keys = new ProofKeys();
        var result = await Verifier(keys).Verify(Bearer(keys, did, curve), CancellationToken.None);
        Assert.Equal(ServiceProofStatus.Valid, result.Status);
        Assert.Equal(did, result.Proof!.IssuerDid);
        Assert.Equal(32, result.Proof.Jti.Length);
        Assert.True(result.Proof.ExpiresAt.ToUnixTimeSeconds() > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Boundary_timestamps_are_accepted_and_the_outer_window_is_rejected()
    {
        var keys = new ProofKeys();
        var did = "did:plc:mcptestboundaaaaaaaaaaaaa";
        var verifier = Verifier(keys, now: FixedNow);
        string Proof(long expiresAt, long issuedAt)
            => "Bearer " + keys.Token(did, payloadJson: keys.DefaultPayload(did, expiresAt: expiresAt, issuedAt: issuedAt));
        Assert.Equal(ServiceProofStatus.Valid, (await verifier.Verify(Proof(FixedNow + 300, FixedNow + 30), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Valid, (await verifier.Verify(Proof(FixedNow + 1, FixedNow - 300), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(Proof(FixedNow, FixedNow - 5), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(Proof(FixedNow + 301, FixedNow - 5), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(Proof(FixedNow + 120, FixedNow - 301), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(Proof(FixedNow + 120, FixedNow + 31), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Wrong_audience_method_alg_or_signature_is_rejected()
    {
        var keys = new ProofKeys();
        var did = "did:plc:mcptestwrongaaaaaaaaaaaaa";
        var verifier = Verifier(keys);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify("Bearer " + keys.Token(did,
            payloadJson: keys.DefaultPayload(did, audience: Authority)), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify("Bearer " + keys.Token(did,
            payloadJson: keys.DefaultPayload(did, method: SpacesOptions.AccessMethod)), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify("Bearer " + keys.Token(did,
            payloadJson: keys.DefaultPayload(did, method: McpAuthenticationConstants.ExchangeMethod + "x")), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify("Bearer " + keys.Token(did,
            headerJson: """{"typ":"JWT","alg":"ES256"}"""), CancellationToken.None)).Status);
        var characters = keys.Token(did).ToCharArray();
        characters[10] = characters[10] == 'A' ? 'B' : 'A';
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify("Bearer " + new string(characters), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Hostile_proof_shapes_are_rejected()
    {
        var keys = new ProofKeys();
        var did = "did:plc:mcptesthostileaaaaaaaaaa";
        var verifier = Verifier(keys, now: FixedNow);
        var baseClaims = $$"""{"iss":"{{did}}","aud":"{{ManagingApp}}","lxm":"{{McpAuthenticationConstants.ExchangeMethod}}","exp":{{FixedNow + 120}},"iat":{{FixedNow - 5}},"jti":"j1"}""";
        string With(string payload) => "Bearer " + keys.Token(did, payloadJson: payload);
        string WithHeader(string header) => "Bearer " + keys.Token(did, headerJson: header, payloadJson: baseClaims);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(With(baseClaims.Replace(",\"jti\"", ",\"sub\":\"did:plc:zzzzzzzzzzzzzzzzzzzzzzzz\",\"jti\"")), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Valid, (await verifier.Verify(With(baseClaims), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(With(baseClaims.Replace("\"iss\":\"" + did + "\"", "\"iss\":\"" + did + "\",\"iss\":\"https://evil\"")), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(With(baseClaims.Replace("\"jti\":\"j1\"", "\"jti\":\"" + new string('x', 129) + "\"")), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(With(baseClaims.Replace("\"jti\":\"j1\"", "\"jti\":\"a\\u0001b\"")), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(With(baseClaims.Replace(",\"jti\":\"j1\"", "")), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(With(baseClaims.Replace("\"iat\":1799999995,", "")), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(With(baseClaims.Replace("}", ",\"nbf\":" + (FixedNow + 1) + "}")), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(WithHeader("""{"typ":"JWT","alg":"ES256K","crit":["exp"]}"""), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(WithHeader("""{"typ":"at+jwt","alg":"ES256K"}"""), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(WithHeader("""{"typ":"JWT"}"""), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Valid, (await verifier.Verify("bearer " + keys.Token(did, payloadJson: baseClaims), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify("Bearer " + keys.Token(did,
            payloadJson: $$"""{"iss":"{{did}}","aud":["{{ManagingApp}}"],"lxm":"{{McpAuthenticationConstants.ExchangeMethod}}","exp":{{FixedNow + 120}},"iat":{{FixedNow - 5}},"jti":"j1"}"""), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify("Bearer " + keys.Token(did, payloadJson: baseClaims) + new string('A', 8192), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify("Bearer not-a-jwt", CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify("Bearer .", CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify("Bearer a.b.c.d", CancellationToken.None)).Status);
        // A huge numeric claim must fail closed as a malformed proof, not surface as a server error.
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(With(baseClaims.Replace($"\"exp\":{FixedNow + 120}", "\"exp\":99999999999999999999")), CancellationToken.None)).Status);
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify(With(baseClaims.Replace($"\"exp\":{FixedNow + 120}", $"\"exp\":\"{FixedNow + 120}\"")), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Unconfigured_audience_and_unreachable_resolution_are_distinguished()
    {
        var keys = new ProofKeys();
        var did = "did:plc:mcptestunconfaaaaaaaaaaa";
        Assert.Equal(ServiceProofStatus.Unconfigured, (await Verifier(keys, managingApp: "").Verify(Bearer(keys, did), CancellationToken.None)).Status);
        var unreachable = new ServiceProofAuthentication(new ThrowingKeySource(),
            Options.Create(new SpacesOptions { ManagingApp = ManagingApp }),
            new FixedTime(DateTimeOffset.UtcNow), NullLogger<ServiceProofAuthentication>.Instance);
        Assert.Equal(ServiceProofStatus.Unreachable, (await unreachable.Verify(Bearer(keys, did), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Expiry_crossing_during_issuer_resolution_is_rechecked()
    {
        var keys = new ProofKeys();
        var did = "did:plc:mcptestresraceaaaaaaaaaaa";
        // The claims clock read sees a valid proof; the post-resolution read advances past exp.
        var stepping = new SteppingClock(DateTimeOffset.FromUnixTimeSeconds(FixedNow), TimeSpan.FromSeconds(120));
        var verifier = new ServiceProofAuthentication(new FakeKeySource(keys),
            Options.Create(new SpacesOptions { ManagingApp = ManagingApp }), stepping,
            NullLogger<ServiceProofAuthentication>.Instance);
        var token = keys.Token(did, payloadJson: keys.DefaultPayload(did, expiresAt: FixedNow + 60, issuedAt: FixedNow - 5));
        Assert.Equal(ServiceProofStatus.Invalid, (await verifier.Verify("Bearer " + token, CancellationToken.None)).Status);
    }

    private sealed class SteppingClock(DateTimeOffset start, TimeSpan step) : TimeProvider
    {
        private DateTimeOffset current = start;
        public override DateTimeOffset GetUtcNow()
        {
            var value = current;
            current += step;
            return value;
        }
    }

    [Fact]
    public void Manage_grant_is_bounded_and_requires_explicit_permission()
    {
        var did = "did:plc:mcptestmanageaaaaaaaaaaa";
        var now = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        var grants = new[] { ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post, ParticipationGrants.Manage };
        Assert.Throws<ArgumentException>(() => ParticipantCredential.Issue(did, "mcp", 1, grants, now));
        var (credential, _) = ParticipantCredential.Issue(did, "mcp", 1, grants, now, managementPermitted: true);
        Assert.Equal(grants, credential.Grants);
        Assert.Throws<ArgumentException>(() => ParticipantCredential.Issue(did, "mcp", 1,
            [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post, ParticipationGrants.Manage, ParticipationGrants.Manage], now, true));
        var principal = ParticipationCredentials.Principal(credential);
        Assert.Equal(did, ParticipationAccess.Require(principal, ParticipationGrants.Manage));
    }

    [Fact]
    public async Task Local_invitation_and_receipt_commit_together_and_survive_a_lost_response()
    {
        await using var fixture = await HostFixture.StartAsync();
        const string owner = "did:plc:mcptestowneraaaaaaaaaaa";
        const string target = "did:plc:mcptestinviteaaaaaaaaaa";
        await fixture.Exchange.Exchange(Bearer(fixture.Keys, owner), null, CancellationToken.None);
        var tangents = fixture.Host.Services.GetRequiredService<TangentSpace.Communities.TangentGovernance>();
        var companions = fixture.Host.Services.GetRequiredService<TangentSpace.Communities.CompanionGovernance>();
        var requests = fixture.Host.Services.GetRequiredService<TangentSpace.Mcp.McpRequests>();
        await tangents.Create(owner, "atomic-invites", "Atomic invitations", null, null, null, null, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => requests.Run<int>("runtime", owner, "invite-once", async () =>
        {
            var registered = await requests.Register("runtime", owner, "invite-once", "InviteParticipant", "atomic-invites",
                new Dictionary<string, string?> { ["target"] = target }, CancellationToken.None);
            requests.CompleteWithDomain(registered.Record, raw => ("completed", null,
                ((TangentSpace.Communities.TangentInvitationResult)raw!).InvitationId));
            await companions.Invite(owner, "atomic-invites", target, TangentSpace.Communities.CompanionRole.Member, CancellationToken.None);
            throw new IOException("Simulated lost response after commit");
        }, CancellationToken.None));
        var receipt = await requests.Find("runtime", owner, "invite-once", CancellationToken.None);
        Assert.Equal("completed", receipt!.State);
        using (EntityContext.NoCache())
        {
            var invitations = await TangentSpace.Communities.TangentInvitation.Query(i => i.TangentKey == "atomic-invites", CancellationToken.None);
            Assert.Single(invitations);
            Assert.Equal(invitations[0].Id, receipt.ResultData);
        }
        var retry = await requests.Run("runtime", owner, "invite-once", () => requests.Register("runtime", owner, "invite-once",
            "InviteParticipant", "atomic-invites", new Dictionary<string, string?> { ["target"] = target }, CancellationToken.None), CancellationToken.None);
        Assert.True(retry.Reused);
        Assert.Equal("completed", retry.Record.State);
    }

    [Fact]
    public async Task Failed_receipt_staging_rolls_back_the_invitation_as_well()
    {
        await using var fixture = await HostFixture.StartAsync();
        const string owner = "did:plc:mcptestowneraaaaaaaaaaa";
        await fixture.Exchange.Exchange(Bearer(fixture.Keys, owner), null, CancellationToken.None);
        var tangents = fixture.Host.Services.GetRequiredService<TangentSpace.Communities.TangentGovernance>();
        var companions = fixture.Host.Services.GetRequiredService<TangentSpace.Communities.CompanionGovernance>();
        var requests = fixture.Host.Services.GetRequiredService<TangentSpace.Mcp.McpRequests>();
        await tangents.Create(owner, "atomic-rollback", "Atomic rollback", null, null, null, null, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => requests.Run<int>("runtime", owner, "failed-invite", async () =>
        {
            var registered = await requests.Register("runtime", owner, "failed-invite", "InviteParticipant", "atomic-rollback",
                new Dictionary<string, string?>(), CancellationToken.None);
            requests.CompleteWithDomain(registered.Record, _ => throw new IOException("Simulated receipt failure"));
            await companions.Invite(owner, "atomic-rollback", "did:plc:mcptestinviteaaaaaaaaaa", TangentSpace.Communities.CompanionRole.Member, CancellationToken.None);
            return 0;
        }, CancellationToken.None));
        using (EntityContext.NoCache())
            Assert.Empty(await TangentSpace.Communities.TangentInvitation.Query(i => i.TangentKey == "atomic-rollback", CancellationToken.None));
        var receipt = await requests.Find("runtime", owner, "failed-invite", CancellationToken.None);
        Assert.Equal("pending", receipt!.State);
        Assert.Null(receipt.ResultData);
    }

    [Fact]
    public async Task First_verified_arrival_claims_blank_owner_once_and_survives_restart()
    {
        const string first = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
        const string second = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
        string database;
        await using (var fixture = await HostFixture.StartAsync(ownerDid: ""))
        {
            database = fixture.DatabasePath;
            var arrival = fixture.Host.Services.GetRequiredService<TangentSpace.Site.Arrival>();
            await arrival.Enter(first, "first.test", CancellationToken.None);
            await arrival.Enter(second, "second.test", CancellationToken.None);
            using (EntityContext.NoCache())
                Assert.Equal(first, (await TangentSpace.Site.TangentSite.Get(TangentSpace.Infrastructure.TangentConstants.SiteId, CancellationToken.None))!.OwnerDid);
        }
        await using (var fixture = await HostFixture.StartAsync(database, ownerDid: ""))
        {
            var arrival = fixture.Host.Services.GetRequiredService<TangentSpace.Site.Arrival>();
            await arrival.CheckConfiguration(CancellationToken.None);
            await arrival.Enter(second, "second.test", CancellationToken.None);
            using (EntityContext.NoCache())
                Assert.Equal(first, (await TangentSpace.Site.TangentSite.Get(TangentSpace.Infrastructure.TangentConstants.SiteId, CancellationToken.None))!.OwnerDid);
        }
    }

    [Fact]
    public async Task Concurrent_first_arrivals_choose_one_owner_and_preserve_both_participants()
    {
        await using var fixture = await HostFixture.StartAsync(ownerDid: "");
        string[] dids = ["did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb"];
        var arrival = fixture.Host.Services.GetRequiredService<TangentSpace.Site.Arrival>();
        await Task.WhenAll(dids.Select(did => arrival.Enter(did, null, CancellationToken.None)));
        using (EntityContext.NoCache())
        {
            var site = await TangentSpace.Site.TangentSite.Get(TangentSpace.Infrastructure.TangentConstants.SiteId, CancellationToken.None);
            Assert.Contains(site!.OwnerDid, dids);
            foreach (var did in dids) Assert.NotNull(await Participant.Get(did, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Explicit_owner_keeps_first_visitor_from_claiming_a_fresh_site()
    {
        const string owner = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
        await using var fixture = await HostFixture.StartAsync(ownerDid: owner);
        var arrival = fixture.Host.Services.GetRequiredService<TangentSpace.Site.Arrival>();
        await arrival.Enter("did:plc:bbbbbbbbbbbbbbbbbbbbbbbb", null, CancellationToken.None);
        using (EntityContext.NoCache())
            Assert.Null(await TangentSpace.Site.TangentSite.Get(TangentSpace.Infrastructure.TangentConstants.SiteId, CancellationToken.None));
        await arrival.Enter(owner, null, CancellationToken.None);
        using (EntityContext.NoCache())
            Assert.Equal(owner, (await TangentSpace.Site.TangentSite.Get(TangentSpace.Infrastructure.TangentConstants.SiteId, CancellationToken.None))!.OwnerDid);
    }

    [Fact]
    public async Task Companion_and_context_bindings_are_distinct_and_cannot_cross_credentials_or_servers()
    {
        await using var fixture = await HostFixture.StartAsync();
        const string did = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
        var first = await fixture.Exchange.Exchange(Bearer(fixture.Keys, did), null, CancellationToken.None);
        var second = await fixture.Exchange.Exchange(Bearer(fixture.Keys, did), null, CancellationToken.None);
        var credentials = fixture.Host.Services.GetRequiredService<ParticipationCredentials>();
        var principal = (await credentials.Authenticate(first.Issued!.Token, CancellationToken.None))!;
        var other = (await credentials.Authenticate(second.Issued!.Token, CancellationToken.None))!;
        var contexts = fixture.Host.Services.GetRequiredService<TangentSpace.Mcp.McpContexts>();
        var selected = await contexts.Select(principal, did, CancellationToken.None);
        Assert.StartsWith("cmp_", selected.Companion.Id);
        await Assert.ThrowsAsync<TangentSpace.Mcp.McpContextExpiredException>(() => contexts.Resolve(principal, selected.Companion.Id, CancellationToken.None));
        await Assert.ThrowsAsync<TangentSpace.Mcp.McpContextExpiredException>(() => contexts.SelectionOf(other, selected.Companion.Id, CancellationToken.None));
        var arrived = await contexts.Bind(selected.Companion, CancellationToken.None);
        Assert.StartsWith("ctx_", arrived.Context.Id);
        Assert.Equal(selected.Companion.Id, arrived.Context.CompanionId);
        Assert.Equal("http://127.0.0.1:5220", arrived.Context.Origin);
        Assert.Equal(arrived.Context.Id, (await contexts.Bind(selected.Companion, CancellationToken.None)).Context.Id);
        await Assert.ThrowsAsync<TangentSpace.Mcp.McpContextExpiredException>(() => contexts.Resolve(other, arrived.Context.Id, CancellationToken.None));
        using (EntityContext.NoCache())
        {
            var saved = await TangentSpace.Mcp.McpContext.Get(arrived.Context.Id, CancellationToken.None);
            saved!.Origin = "https://another.tangent.example";
            await saved.Save(CancellationToken.None);
        }
        await Assert.ThrowsAsync<TangentSpace.Mcp.McpContextExpiredException>(() => contexts.Resolve(principal, arrived.Context.Id, CancellationToken.None));
        var cookie = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AtprotoClaimTypes.Did, did)], "atproto"));
        await credentials.Revoke(cookie, first.Issued.Credential.Id, CancellationToken.None);
        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => contexts.SelectionOf(principal, selected.Companion.Id, CancellationToken.None));
    }

    private sealed class HostFixture : IAsyncDisposable
    {
        public required IntegrationHost Host { get; init; }
        public required string DatabasePath { get; init; }
        public required ProofKeys Keys { get; init; }

        public static async Task<HostFixture> StartAsync(string? databasePath = null, string ownerDid = "did:plc:mcptestowneraaaaaaaaaaa")
        {
            var keys = new ProofKeys();
            var root = Path.Combine(Path.GetTempPath(), "TangentSpace-McpAuth", Guid.CreateVersion7().ToString("n"));
            Directory.CreateDirectory(root);
            var database = databasePath ?? Path.Combine(root, "proofs.sqlite");
            var host = await KoanIntegrationHost.Configure()
                .WithSettings(new Dictionary<string, string?>
                {
                    ["Tangent:Site:Name"] = "MCP Proof Test Site",
                    ["Tangent:Site:OwnerDid"] = ownerDid,
                    ["Tangent:Mcp:PublicBaseUrl"] = "http://127.0.0.1:5220",
                    ["Tangent:Spaces:AuthorityDid"] = Authority,
                    ["Tangent:Spaces:ManagingApp"] = ManagingApp,
                    ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                    ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={database}",
                    ["Koan:Data:Sqlite:ConnectionString"] = $"Data Source={database}"
                })
                .ConfigureServices(services =>
                {
                    services.AddKoan();
                    services.AddTangentMcpAuthentication();
                    services.AddSingleton<IServiceProofKeySource>(new FakeKeySource(keys));
                })
                .StartAsync();
            AppHost.Current = host.Services;
            return new HostFixture { Host = host, DatabasePath = database, Keys = keys };
        }

        public ServiceProofExchange Exchange => Host.Services.GetRequiredService<ServiceProofExchange>();

        public async ValueTask DisposeAsync()
        {
            if (ReferenceEquals(AppHost.Current, Host.Services)) AppHost.Current = null;
            TestHooks.ResetDataConfigs();
            await Host.DisposeAsync();
            TestHooks.ResetDataConfigs();
        }
    }

    [Fact]
    public async Task Fresh_verified_identity_enrolls_autonomously_and_receives_a_one_day_credential()
    {
        await using var fixture = await HostFixture.StartAsync();
        var did = "did:plc:mcptestfreshaaaaaaaaaaa";
        var result = await fixture.Exchange.Exchange(Bearer(fixture.Keys, did), null, CancellationToken.None);
        Assert.Equal(ServiceProofExchangeStatus.Issued, result.Status);
        Assert.StartsWith("ts_", result.Issued!.Token);
        using (EntityContext.NoCache())
        {
            var participant = await Participant.Get(did, CancellationToken.None);
            Assert.NotNull(participant);
            Assert.Null(participant!.Handle);
            Assert.False(participant.IsSuspended);
        }
        var credential = result.Issued.Credential;
        Assert.Equal(did, credential.Did);
        Assert.Equal([ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post], credential.Grants);
        Assert.Equal(TimeSpan.FromDays(1), credential.ExpiresAt - credential.CreatedAt);
    }

    [Fact]
    public async Task Issued_token_authenticates_and_revocation_applies_immediately()
    {
        await using var fixture = await HostFixture.StartAsync();
        var did = "did:plc:mcptestrevokeaaaaaaaaaaa";
        var issued = await fixture.Exchange.Exchange(Bearer(fixture.Keys, did), null, CancellationToken.None);
        Assert.Equal(ServiceProofExchangeStatus.Issued, issued.Status);
        var credentials = fixture.Host.Services.GetRequiredService<ParticipationCredentials>();
        var principal = await credentials.Authenticate(issued.Issued!.Token, CancellationToken.None);
        Assert.NotNull(principal);
        Assert.Equal(did, principal!.FindFirst(AtprotoClaimTypes.Did)!.Value);
        Assert.Contains(principal.Claims, claim => claim.Type == ParticipationConstants.GrantClaim && claim.Value == ParticipationGrants.Post);
        var cookie = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AtprotoClaimTypes.Did, did)], "atproto"));
        Assert.True(await credentials.Revoke(cookie, issued.Issued.Credential.Id, CancellationToken.None));
        Assert.Null(await credentials.Authenticate(issued.Issued.Token, CancellationToken.None));
    }

    [Fact]
    public async Task Replayed_jti_is_rejected_while_a_new_proof_succeeds()
    {
        await using var fixture = await HostFixture.StartAsync();
        var did = "did:plc:mcptestreplayaaaaaaaaaaa";
        var token = fixture.Keys.Token(did);
        Assert.Equal(ServiceProofExchangeStatus.Issued,
            (await fixture.Exchange.Exchange("Bearer " + token, null, CancellationToken.None)).Status);
        Assert.Equal(ServiceProofExchangeStatus.ReplayedProof,
            (await fixture.Exchange.Exchange("Bearer " + token, null, CancellationToken.None)).Status);
        Assert.Equal(ServiceProofExchangeStatus.Issued,
            (await fixture.Exchange.Exchange(Bearer(fixture.Keys, did), null, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Replay_and_credential_survive_a_host_restart()
    {
        var did = "did:plc:mcptestrestartaaaaaaaaaa";
        string database, savedToken;
        await using (var fixture = await HostFixture.StartAsync())
        {
            database = fixture.DatabasePath;
            var issued = await fixture.Exchange.Exchange(Bearer(fixture.Keys, did), null, CancellationToken.None);
            Assert.Equal(ServiceProofExchangeStatus.Issued, issued.Status);
            savedToken = issued.Issued!.Token;
        }
        await using (var fixture = await HostFixture.StartAsync(database))
        {
            var token = fixture.Keys.Token(did, payloadJson: fixture.Keys.DefaultPayload(did, jti: "restart-fixed-jti"));
            Assert.Equal(ServiceProofExchangeStatus.Issued,
                (await fixture.Exchange.Exchange("Bearer " + token, null, CancellationToken.None)).Status);
            Assert.Equal(ServiceProofExchangeStatus.ReplayedProof,
                (await fixture.Exchange.Exchange("Bearer " + token, null, CancellationToken.None)).Status);
            var credentials = fixture.Host.Services.GetRequiredService<ParticipationCredentials>();
            Assert.NotNull(await credentials.Authenticate(savedToken, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Suspended_identity_cannot_exchange_a_new_proof()
    {
        await using var fixture = await HostFixture.StartAsync();
        var did = "did:plc:mcptestsuspendaaaaaaaaaa";
        Assert.Equal(ServiceProofExchangeStatus.Issued,
            (await fixture.Exchange.Exchange(Bearer(fixture.Keys, did), null, CancellationToken.None)).Status);
        using (EntityContext.NoCache())
        {
            var participant = await Participant.Get(did, CancellationToken.None);
            participant!.IsSuspended = true;
            await participant.Save(CancellationToken.None);
        }
        Assert.Equal(ServiceProofExchangeStatus.Suspended,
            (await fixture.Exchange.Exchange(Bearer(fixture.Keys, did), null, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Manage_grant_is_issued_to_any_active_participant_on_explicit_request()
    {
        await using var fixture = await HostFixture.StartAsync();
        var request = new ServiceProofExchangeRequest
        {
            Grants = [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post, ParticipationGrants.Manage]
        };
        var participant = "did:plc:mcptestoutsideaaaaaaaaa";
        var granted = await fixture.Exchange.Exchange(Bearer(fixture.Keys, participant), request, CancellationToken.None);
        Assert.Equal(ServiceProofExchangeStatus.Issued, granted.Status);
        Assert.Contains(ParticipationGrants.Manage, granted.Issued!.Credential.Grants);
        var principal = await fixture.Host.Services.GetRequiredService<ParticipationCredentials>()
            .Authenticate(granted.Issued.Token, CancellationToken.None);
        Assert.Equal(participant, ParticipationAccess.Require(principal!, ParticipationGrants.Manage));
        var plain = await fixture.Exchange.Exchange(Bearer(fixture.Keys, participant), null, CancellationToken.None);
        Assert.Equal(ServiceProofExchangeStatus.Issued, plain.Status);
        Assert.DoesNotContain(ParticipationGrants.Manage, plain.Issued!.Credential.Grants);
    }

    [Fact]
    public async Task Expired_proof_is_rejected_at_issuance_even_after_passing_verification()
    {
        await using var fixture = await HostFixture.StartAsync();
        var did = "did:plc:mcptestgateexpaaaaaaaaaaa";
        var expired = new ServiceProof(did, "gate-expired-jti", DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Equal(ServiceProofExchangeStatus.InvalidProof,
            (await fixture.Exchange.Complete(expired, null, CancellationToken.None)).Status);
        using (EntityContext.NoCache())
        {
            Assert.Null(await ServiceProofReplayRecord.Get(ServiceProofReplayRecord.Key(did, "gate-expired-jti"), CancellationToken.None));
            Assert.Empty(await ParticipantCredential.Query(value => value.ParticipantDid == did, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Suspension_is_rechecked_inside_the_issuance_transaction()
    {
        await using var fixture = await HostFixture.StartAsync();
        var did = "did:plc:mcptestgatesusaaaaaaaaaa";
        using (EntityContext.NoCache())
        {
            var participant = await Participant.Get(did, CancellationToken.None) ?? Participant.FirstArrival(did, null, DateTimeOffset.UtcNow);
            participant.IsSuspended = true;
            await participant.Save(CancellationToken.None);
        }
        var validProof = new ServiceProof(did, "gate-suspended-jti", DateTimeOffset.UtcNow.AddSeconds(120));
        Assert.Equal(ServiceProofExchangeStatus.Suspended,
            (await fixture.Exchange.Complete(validProof, null, CancellationToken.None)).Status);
        using (EntityContext.NoCache())
        {
            Assert.Null(await ServiceProofReplayRecord.Get(ServiceProofReplayRecord.Key(did, "gate-suspended-jti"), CancellationToken.None));
            Assert.Empty(await ParticipantCredential.Query(value => value.ParticipantDid == did, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Existing_display_handle_is_preserved_and_did_is_the_fallback()
    {
        await using var fixture = await HostFixture.StartAsync();
        var did = "did:plc:mcptesthandleaaaaaaaaaaa";
        using (EntityContext.NoCache())
            await Participant.FirstArrival(did, "preserved.test", DateTimeOffset.UtcNow).Save(CancellationToken.None);
        Assert.Equal(ServiceProofExchangeStatus.Issued,
            (await fixture.Exchange.Exchange(Bearer(fixture.Keys, did), null, CancellationToken.None)).Status);
        using (EntityContext.NoCache())
            Assert.Equal("preserved.test", (await Participant.Get(did, CancellationToken.None))!.Handle);
    }

    [Fact]
    public async Task Controller_maps_sanitized_results_and_never_returns_the_proof()
    {
        await using var fixture = await HostFixture.StartAsync();
        var did = "did:plc:mcptestctrlaaaaaaaaaaaaa";
        var proof = Bearer(fixture.Keys, did);
        ServiceProofTokenController WithHeader(string? authorization)
        {
            var controller = new ServiceProofTokenController(fixture.Exchange, NullLogger<ServiceProofTokenController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            if (authorization is not null) controller.HttpContext.Request.Headers.Authorization = authorization;
            return controller;
        }
        var tokenController = WithHeader(proof);
        var issued = await tokenController.Exchange(null, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(issued);
        Assert.Contains("ts_", JsonSerializer.Serialize(ok.Value));
        Assert.Equal("no-store", tokenController.Response.Headers.CacheControl.ToString());
        var missing = await WithHeader(null).Exchange(null, CancellationToken.None);
        Assert.Equal(StatusCodes.Status401Unauthorized, ((ObjectResult)missing).StatusCode);
        var wrongAudience = "Bearer " + fixture.Keys.Token(did, payloadJson: fixture.Keys.DefaultPayload(did, audience: Authority));
        var rejected = await WithHeader(wrongAudience).Exchange(null, CancellationToken.None);
        Assert.Equal(StatusCodes.Status401Unauthorized, ((ObjectResult)rejected).StatusCode);
        var replay = await WithHeader(proof).Exchange(null, CancellationToken.None);
        Assert.Equal(StatusCodes.Status401Unauthorized, ((ObjectResult)replay).StatusCode);
        var bodies = JsonSerializer.Serialize(((ObjectResult)rejected).Value)
            + JsonSerializer.Serialize(((ObjectResult)missing).Value)
            + JsonSerializer.Serialize(((ObjectResult)replay).Value);
        Assert.DoesNotContain(proof[7..], bodies);
        Assert.DoesNotContain(wrongAudience[7..], bodies);
        Assert.DoesNotContain("ts_", bodies);
    }

    [Fact]
    public void Discovery_pins_the_configured_origin_audience_and_profile()
    {
        var spaces = new SpacesOptions { AuthorityDid = Authority, ManagingApp = ManagingApp };
        var mcp = new TangentSpace.Mcp.McpOptions { PublicBaseUrl = "http://127.0.0.1:5220" };
        var controller = new TangentMcpDiscoveryController(Options.Create(spaces), Options.Create(mcp))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Request = { Headers = { Host = "evil.example" } } }
            }
        };
        var ok = Assert.IsType<OkObjectResult>(controller.Discover());
        var body = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("http://127.0.0.1:5220/mcp/token", body);
        Assert.Contains("http://127.0.0.1:5220/mcp", body);
        Assert.Contains(McpAuthenticationConstants.ExchangeMethod, body);
        Assert.Contains(ManagingApp, body);
        Assert.Contains(McpAuthenticationConstants.Profile, body);
        Assert.Contains(McpAuthenticationConstants.ProtocolVersion, body);
        Assert.DoesNotContain("evil.example", body);
        Assert.Contains("\"includedInProof\":false", body);
        Assert.Contains("\"standardMcpOAuthAuthorizationSupport\":false", body);
        Assert.Contains("independently verifies current authority", body);
        Assert.Contains("/api/connections/rooms", body);
        var unconfiguredAudience = new TangentMcpDiscoveryController(Options.Create(new SpacesOptions()), Options.Create(mcp))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ((ObjectResult)unconfiguredAudience.Discover()).StatusCode);
        foreach (var invalid in new[] { "", "   ", "ftp://example.com", "http://user:pass@127.0.0.1:5220", "http://127.0.0.1:5220/evil", "http://127.0.0.1:5220?x=1" })
        {
            var noOrigin = new TangentMcpDiscoveryController(Options.Create(spaces),
                Options.Create(new TangentSpace.Mcp.McpOptions { PublicBaseUrl = invalid }))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, ((ObjectResult)noOrigin.Discover()).StatusCode);
        }
    }
}
