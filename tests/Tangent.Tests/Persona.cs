using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Koan.Data.Abstractions;
using Koan.Web.Auth.Domain;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Participants;

namespace Tangent;

/// <summary>Someone a proof names: a participant of the place's own id, a GUID made from their name so every proof that
/// names them names the same person, bound to a Bluesky account of their own, <c>did:plc:</c> and their name, whose
/// handle is their name and <c>.example</c>. A persona's logins are bound to their id before they sign in
/// (<see cref="Bind"/>), so a sign-in as them arrives as them; a DID no persona holds signs in as a newcomer, whom Koan
/// gives an id of its own.</summary>
public static class Persona
{
    private static readonly ConcurrentDictionary<string, string> Names = new(StringComparer.Ordinal);

    /// <summary>The id of the persona called <paramref name="name"/>.</summary>
    public static string Id(string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(name));
        bytes[7] = (byte)(0x70 | (bytes[7] & 0x0F));
        bytes[8] = (byte)(0x80 | (bytes[8] & 0x3F));
        var id = new Guid(bytes).ToString("D");
        Names[id] = name;
        return id;
    }

    /// <summary>Whether <paramref name="id"/> is a persona's: anything a proof names as a participant but a DID, which is
    /// an account. A proof that only needs one participant may name them by a plain word; a mention names a GUID.</summary>
    public static bool Is(string id) => !id.StartsWith("did:", StringComparison.Ordinal);

    /// <summary>The DID of the account the persona <paramref name="id"/> is bound to; a DID itself for a DID.</summary>
    public static string Did(string id) => id.StartsWith("did:", StringComparison.Ordinal) ? id : "did:plc:" + Name(id);

    /// <summary>The handle of the persona <paramref name="id"/>.</summary>
    public static string Handle(string id) => Name(id) + ".example";

    private static string Name(string id) => Names.GetValueOrDefault(id) ?? id;

    /// <summary>Binds the logins of the persona's account, by Koan's test connector and by atproto, to their id, as a
    /// sign-in with either would find them.</summary>
    public static async Task Bind(TangentSpaceFixture tangentSpace, string id, CancellationToken ct)
    {
        var store = tangentSpace.Services.GetRequiredService<IExternalIdentityStore>();
        var signedIn = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "persona"));
        var did = Did(id);
        foreach (var provider in new[] { "test", "atproto" })
        {
            var outcome = await store.Bind(signedIn, new ExternalIdentity
            {
                Subject = did,
                Provider = provider,
                ProviderKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(did)))
            }, ct);
            if (outcome != BindOutcome.Bound) throw new InvalidOperationException($"{did} by {provider} is not {id}'s: {outcome}.");
        }
    }

    /// <summary>The participant bound to the account <paramref name="did"/>, as a newcomer's sign-in made them.</summary>
    public static async Task<Participant?> Arrived(string did, CancellationToken ct)
        => (await Participant.Query(participant => participant.Did == did, new QueryDefinition { Page = 1, PageSize = 1 }, ct)).FirstOrDefault();
}
