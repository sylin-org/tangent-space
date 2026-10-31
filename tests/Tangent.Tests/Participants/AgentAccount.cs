using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.EC;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using BcInteger = Org.BouncyCastle.Math.BigInteger;

namespace Tangent.Participants;

/// <summary>An atproto account an agent holds: its DID, its handle and its signing key, minting service-auth tokens as its
/// account's server does.</summary>
public sealed class AgentAccount(string did, string handle)
{
    private const string Base58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    private readonly SigningKeys keys = SigningKeys.Generate();

    public string Did { get; } = did;

    public string Handle { get; } = handle;

    /// <summary>A service-auth token for <paramref name="audience"/>, bound to <paramref name="method"/>, a minute's life
    /// unless <paramref name="livedFor"/> says it ended that long ago.</summary>
    public string Token(string audience, string method, TimeSpan? livedFor = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)(livedFor ?? TimeSpan.Zero).TotalSeconds;
        var header = Encode(JsonSerializer.Serialize(new { typ = "JWT", alg = "ES256" }));
        var payload = Encode(JsonSerializer.Serialize(new { iss = Did, aud = audience, lxm = method, iat = now, exp = now + 60, jti = Guid.NewGuid().ToString("N") }));
        return header + "." + payload + "." + WebEncoders.Base64UrlEncode(keys.Sign(Encoding.ASCII.GetBytes(header + "." + payload)));
    }

    /// <summary>The DID document a directory serves for this account, as it stands on the network.</summary>
    public object Document() => new
    {
        id = Did,
        alsoKnownAs = new[] { "at://" + Handle },
        verificationMethod = new[] { new { id = "#atproto", type = "Multikey", controller = Did, publicKeyMultibase = "z" + EncodeBase58(keys.Multikey()) } },
    };

    private static string Encode(string json) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(json));

    private static string EncodeBase58(byte[] bytes)
    {
        var number = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var encoded = new StringBuilder();
        while (number > 0)
        {
            encoded.Insert(0, Base58[(int)(number % 58)]);
            number /= 58;
        }

        return encoded.ToString();
    }

    private sealed class SigningKeys
    {
        private readonly ECDomainParameters domain;
        private readonly ECPrivateKeyParameters secret;
        private readonly ECPublicKeyParameters published;

        private SigningKeys(ECDomainParameters domain, ECPrivateKeyParameters secret, ECPublicKeyParameters published)
        {
            this.domain = domain;
            this.secret = secret;
            this.published = published;
        }

        public static SigningKeys Generate()
        {
            var named = CustomNamedCurves.GetByName("secp256r1");
            var domain = new ECDomainParameters(named.Curve, named.G, named.N, named.H);
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(domain, new SecureRandom()));
            var pair = generator.GenerateKeyPair();
            return new SigningKeys(domain, (ECPrivateKeyParameters)pair.Private, (ECPublicKeyParameters)pair.Public);
        }

        public byte[] Multikey() => [0x80, 0x24, .. published.Q.GetEncoded(compressed: true)];

        public byte[] Sign(byte[] input)
        {
            var signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));
            signer.Init(true, secret);
            var parts = signer.GenerateSignature(SHA256.HashData(input));
            var low = parts[1].CompareTo(domain.N.ShiftRight(1)) > 0 ? domain.N.Subtract(parts[1]) : parts[1];
            var compact = new byte[64];
            Write(parts[0], compact, 32);
            Write(low, compact, 64);
            return compact;
        }

        private static void Write(BcInteger value, byte[] into, int end)
        {
            var bytes = value.ToByteArrayUnsigned();
            bytes.CopyTo(into, end - bytes.Length);
        }
    }
}

/// <summary>A loopback PLC directory serving the documents of the accounts it holds, and the settings that point a Tangent
/// Space's atproto connector at it, which only the Development environment allows.</summary>
public sealed class AgentDirectory : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly Dictionary<string, AgentAccount> accounts = new(StringComparer.Ordinal);

    private AgentDirectory(WebApplication app) => this.app = app;

    public static async Task<AgentDirectory> Start(params AgentAccount[] accounts)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var app = builder.Build();
        var directory = new AgentDirectory(app);
        foreach (var account in accounts) directory.accounts[account.Did] = account;
        app.MapGet("/{**did}", (string did) => directory.accounts.TryGetValue(did, out var account) ? Results.Json(account.Document()) : Results.NotFound());
        await app.StartAsync();
        return directory;
    }

    public string Origin => app.Urls.Single();

    /// <summary><paramref name="settings"/> with the connector resolving these accounts here.</summary>
    public Dictionary<string, string?> Settings(Dictionary<string, string?> settings)
    {
        settings["Koan:Web:Auth:Atproto:PlcDirectory"] = Origin;
        settings["Koan:Web:Auth:Atproto:DevelopmentPlcDirectory"] = Origin;
        settings["Koan:Web:Auth:Atproto:DevelopmentAllowedOrigins:0"] = Origin;
        foreach (var account in accounts.Values) settings["Koan:Web:Auth:Atproto:DevelopmentHandles:" + account.Handle] = account.Did;
        return settings;
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
