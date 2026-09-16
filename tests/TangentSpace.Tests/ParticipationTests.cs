using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TangentSpace.Participants;
using TangentSpace.Participation;
using Xunit;
using CookieAuthentication = Koan.Web.Auth.Extensions.AuthenticationExtensions;

namespace TangentSpace.Tests;

public sealed class ParticipationTests
{
    private const string Did = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Other = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly string ParticipantId = TangentSpace.Participants.Participant.NewIdentifier();
    private static readonly string OtherParticipant = TangentSpace.Participants.Participant.NewIdentifier();
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
        Assert.Equal(ParticipantId, credential.ParticipantId);
    }

    [Fact]
    public void Expiry_and_revocation_reject_subsequent_authentication()
    {
        var (credential, _) = Issue();
        Assert.True(credential.IsActive(Now));
        Assert.False(credential.IsActive(Now.AddDays(7)));
        Assert.False(credential.IsActive(Now.AddSeconds(-1)));
        Assert.Throws<UnauthorizedAccessException>(() => credential.Revoke(OtherParticipant, Now));
        Assert.Null(credential.RevokedAt);
        credential.Revoke(ParticipantId, Now.AddHours(1));
        Assert.False(credential.IsActive(Now.AddHours(1)));
        credential.Revoke(ParticipantId, Now.AddHours(2));
        Assert.Equal(Now.AddHours(1), credential.RevokedAt);
    }

    [Theory, InlineData(0), InlineData(-1), InlineData(31)]
    public void Credential_lifetime_cannot_exceed_the_enrollment_bound(int lifetime)
        => Assert.Throws<ArgumentException>(() => ParticipantCredential.Issue(ParticipantId, "runner", lifetime, ["read"], Now));

    [Fact]
    public void Credential_identity_carries_the_participant_claim_and_narrow_grants()
    {
        var (credential, _) = ParticipantCredential.Issue(ParticipantId, "reader", 1, [ParticipationGrants.Read], Now);
        // Without an atproto identity supplied, the bearer principal carries only the participant claim.
        var principal = ParticipationCredentials.Principal(credential);
        Assert.Equal(ParticipantId, ParticipationAccess.Require(principal, ParticipationGrants.Read));
        Assert.Null(principal.FindFirst(AtprotoClaimTypes.Did));
        // With one, both claims travel: the GUID spine always, the DID when held.
        var atproto = ParticipationCredentials.Principal(credential, Did);
        Assert.Equal(Did, atproto.FindFirst(AtprotoClaimTypes.Did)!.Value);
        Assert.Equal(ParticipantId, atproto.FindFirst(ParticipationConstants.ParticipantClaim)!.Value);
        Assert.Throws<UnauthorizedAccessException>(() => ParticipationAccess.Require(principal, ParticipationGrants.Post));
        Assert.Empty(principal.FindAll(ClaimTypes.Role));
        Assert.Throws<ArgumentException>(() => ParticipantCredential.Issue(ParticipantId, "admin", 1, ["admin"], Now));
        Assert.Equal(ParticipantId, ParticipationAccess.Require(Cookie(), ParticipationGrants.Post));
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
        Assert.Equal(ParticipantId, result.Principal!.FindFirst(ParticipationConstants.ParticipantClaim)!.Value);
    }

    private static (ParticipantCredential Credential, string Token) Issue()
        => ParticipantCredential.Issue(ParticipantId, "runner", 7, [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post], Now);

    private static ClaimsPrincipal Cookie() => new(new ClaimsIdentity(
        [
            new Claim(AtprotoClaimTypes.Did, Did),
            new Claim(ParticipationConstants.ParticipantClaim, ParticipantId)
        ], "atproto"));

    private static ServiceProvider AuthenticationServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(options => options.DefaultAuthenticateScheme = CookieAuthentication.CookieScheme)
            .AddScheme<AuthenticationSchemeOptions, CookieFixtureHandler>(CookieAuthentication.CookieScheme, _ => { });
        services.AddParticipation();
        services.AddSingleton<IAtprotoHandleSource, NoAtprotoHandles>();
        services.AddSingleton<TangentSpace.Participants.ParticipantDirectory>();
        return services.BuildServiceProvider();
    }

    private sealed class NoAtprotoHandles : IAtprotoHandleSource
    {
        public Task<string?> HandleOf(string did, CancellationToken ct) => Task.FromResult<string?>(null);
    }

    // This fixture tests scheme selection only; real browser OAuth has a separate end-to-end proof.
    private sealed class CookieFixtureHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(Cookie(), Scheme.Name)));
    }
}
