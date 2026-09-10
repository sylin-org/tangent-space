using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TangentSpace.Participation;
using Xunit;
using CookieAuthentication = Koan.Web.Auth.Extensions.AuthenticationExtensions;

namespace TangentSpace.Tests;

public sealed class ParticipationTests
{
    private const string Did = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Other = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issued_secret_is_random_and_only_its_hash_is_stored()
    {
        var (credential, token) = Issue();
        var (another, secondToken) = Issue();
        Assert.StartsWith("ts_", token);
        Assert.Equal(46, token.Length);
        Assert.Equal(ParticipantCredential.Hash(token), credential.Id);
        Assert.NotEqual(token, secondToken);
        Assert.NotEqual(credential.Id, another.Id);
        Assert.DoesNotContain(token, JsonSerializer.Serialize(credential));
        Assert.Equal(Did, credential.ParticipantDid);
    }

    [Fact]
    public void Expiry_and_revocation_reject_subsequent_authentication()
    {
        var (credential, _) = Issue();
        Assert.True(credential.IsActive(Now));
        Assert.False(credential.IsActive(Now.AddDays(7)));
        Assert.False(credential.IsActive(Now.AddSeconds(-1)));
        Assert.Throws<UnauthorizedAccessException>(() => credential.Revoke(Other, Now));
        Assert.Null(credential.RevokedAt);
        credential.Revoke(Did, Now.AddHours(1));
        Assert.False(credential.IsActive(Now.AddHours(1)));
        credential.Revoke(Did, Now.AddHours(2));
        Assert.Equal(Now.AddHours(1), credential.RevokedAt);
    }

    [Theory, InlineData(0), InlineData(-1), InlineData(31)]
    public void Credential_lifetime_cannot_exceed_the_enrollment_bound(int lifetime)
        => Assert.Throws<ArgumentException>(() => ParticipantCredential.Issue(Did, "runner", lifetime, ["read"], Now));

    [Fact]
    public void Enrollment_is_bound_to_authenticated_claims_and_rejects_submitted_DID()
    {
        Assert.Equal(Did, ParticipationAccess.EnrollmentDid(Cookie()));
        Assert.Throws<UnauthorizedAccessException>(() => ParticipationAccess.EnrollmentDid(new ClaimsPrincipal(new ClaimsIdentity([new Claim(AtprotoClaimTypes.Did, Other)]))));
        var hostile = "{\"name\":\"runner\",\"lifetimeDays\":7,\"did\":\"" + Other + "\"}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CredentialEnrollmentRequest>(hostile));
        Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => Newtonsoft.Json.JsonConvert.DeserializeObject<CredentialEnrollmentRequest>(hostile));
        var (credential, _) = Issue();
        Assert.Throws<UnauthorizedAccessException>(() => ParticipationAccess.EnrollmentDid(ParticipationCredentials.Principal(credential)));
    }

    [Fact]
    public void Mvc_enrollment_defaults_and_unknown_revocation_fields_are_explicit()
    {
        var enrollment = Newtonsoft.Json.JsonConvert.DeserializeObject<CredentialEnrollmentRequest>("{\"name\":\"runner\"}")!;
        Assert.Equal("runner", enrollment.Name);
        Assert.Equal(7, enrollment.LifetimeDays);
        Assert.Null(enrollment.Grants);
        Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => Newtonsoft.Json.JsonConvert.DeserializeObject<CredentialRevocationRequest>("{\"did\":\"" + Other + "\"}"));
        Assert.NotNull(Newtonsoft.Json.JsonConvert.DeserializeObject<CredentialRevocationRequest>("{}"));
    }

    [Fact]
    public void Credential_identity_has_only_its_DID_and_narrow_grants()
    {
        var (credential, _) = ParticipantCredential.Issue(Did, "reader", 1, [ParticipationGrants.Read], Now);
        var principal = ParticipationCredentials.Principal(credential);
        Assert.Equal(Did, ParticipationAccess.Require(principal, ParticipationGrants.Read));
        Assert.Throws<UnauthorizedAccessException>(() => ParticipationAccess.Require(principal, ParticipationGrants.Post));
        Assert.Empty(principal.FindAll(ClaimTypes.Role));
        Assert.Throws<ArgumentException>(() => ParticipantCredential.Issue(Did, "admin", 1, ["admin"], Now));
        Assert.Equal(Did, ParticipationAccess.Require(Cookie(), ParticipationGrants.Post));
    }

    [Theory, InlineData(""), InlineData("Basic test"), InlineData("Bearer invalid"), InlineData("Bearer ts_short")]
    public async Task Any_invalid_Authorization_header_prevents_cookie_fallback(string header)
    {
        using var services = AuthenticationServices();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Authorization = header;
        var result = await context.AuthenticateAsync();
        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
    }

    [Fact]
    public async Task Requests_without_Authorization_use_the_existing_cookie_scheme()
    {
        using var services = AuthenticationServices();
        var context = new DefaultHttpContext { RequestServices = services };
        var result = await context.AuthenticateAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(Did, result.Principal!.FindFirst(AtprotoClaimTypes.Did)!.Value);
    }

    private static (ParticipantCredential Credential, string Token) Issue()
        => ParticipantCredential.Issue(Did, "runner", 7, [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post], Now);

    private static ClaimsPrincipal Cookie() => new(new ClaimsIdentity([new Claim(AtprotoClaimTypes.Did, Did)], "atproto"));

    private static ServiceProvider AuthenticationServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(options => options.DefaultAuthenticateScheme = CookieAuthentication.CookieScheme)
            .AddScheme<AuthenticationSchemeOptions, CookieFixtureHandler>(CookieAuthentication.CookieScheme, _ => { });
        services.AddParticipation();
        return services.BuildServiceProvider();
    }

    // This fixture tests scheme selection only; real browser OAuth has a separate end-to-end proof.
    private sealed class CookieFixtureHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(Cookie(), Scheme.Name)));
    }
}
