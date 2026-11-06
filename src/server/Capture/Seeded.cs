using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Koan.Core.Hosting.App;
using Koan.Web.Auth.Domain;
using Microsoft.AspNetCore.Mvc;
using Tangent.Space;

namespace Tangent.Capture;

/// <summary>The people the capture seeds write: each a participant of the place's own id, fixed so every seed names the
/// same person, bound to a Bluesky DID of its own. The capture persona signs in through Koan's test connector as her
/// DID, so <see cref="Persona"/> binds that login to her id before anyone signs in (<see cref="BindPersona"/>), and her
/// sign-in arrives as the participant the seeds write. Compiled only into the capture host.</summary>
[AnswersBeforeFirstRun]
public sealed class Seeded : ControllerBase
{
    public const string Persona = "0190c0de-0000-7000-8000-000000000001";
    public const string Leo = "0190c0de-0000-7000-8000-000000000002";
    public const string Lumen = "0190c0de-0000-7000-8000-000000000003";
    public const string Mara = "0190c0de-0000-7000-8000-000000000004";
    public const string Noor = "0190c0de-0000-7000-8000-000000000005";
    public const string Wren = "0190c0de-0000-7000-8000-000000000006";
    public const string Tomas = "0190c0de-0000-7000-8000-000000000007";
    public const string Mira = "0190c0de-0000-7000-8000-000000000008";
    public const string Kai = "0190c0de-0000-7000-8000-000000000009";
    public const string June = "0190c0de-0000-7000-8000-00000000000a";

    /// <summary>The DID the capture persona signs in as, which the capture's own registry names too.</summary>
    public const string PersonaDid = "did:plc:capture4persona";

    private static readonly Dictionary<string, string> Dids = new(StringComparer.Ordinal)
    {
        [Persona] = PersonaDid,
        [Leo] = "did:plc:capture4leo",
        [Lumen] = "did:plc:capture4lumen",
        [Mara] = "did:plc:capture4mara",
        [Noor] = "did:plc:capture4noor",
        [Wren] = "did:plc:capture4wren",
        [Tomas] = "did:plc:capture4tomas",
        [Mira] = "did:plc:capture4mira",
        [Kai] = "did:plc:capture4kai",
        [June] = "did:plc:capture4june"
    };

    /// <summary>The DID a seeded person is bound to.</summary>
    public static string Did(string id) => Dids[id];

    /// <summary>Binds the test connector's login of the persona's DID to her id, once, before the place is set up, so her
    /// first sign-in, whichever state signs her in first, arrives as her.</summary>
    [HttpPost("/capture/seed/persona")]
    public async Task<IActionResult> BindPersona(CancellationToken ct)
    {
        var signedIn = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Persona)], "capture"));
        var login = new ExternalIdentity
        {
            Subject = PersonaDid,
            Provider = "test",
            ProviderKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PersonaDid)))
        };
        var outcome = await AppHost.GetRequiredService<IExternalIdentityStore>(nameof(Seeded)).Bind(signedIn, login, ct);
        return outcome == BindOutcome.Bound ? NoContent() : Problem(outcome.ToString());
    }
}
