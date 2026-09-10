using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.AtProtocol;
using TangentSpace.Conversation;
using TangentSpace.Participation;
using Xunit;

namespace TangentSpace.Tests;

public sealed class ConversationRecoveryTests
{
    private const string Did = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("write", "ScopeMissingError", 403)]
    [InlineData("delegate", "ScopeMissingError", 403)]
    [InlineData("write", "insufficient_scope", 403)]
    [InlineData("write", "AuthenticationRequired", 401)]
    [InlineData("delegate", "provider-error", 401)]
    [InlineData("write", "InvalidToken", 400)]
    [InlineData("write", "ExpiredToken", 400)]
    [InlineData("write", "invalid_token", 401)]
    [InlineData("session", "reauthorization-required", 409)]
    public void OAuth_authorization_failure_pauses_background_writes_without_losing_the_original_operation(string stage, string code, int status)
    {
        var intent = Intent();
        var originalContent = intent.Content;
        var originalRecord = intent.RecordKey;
        var originalId = intent.Id;
        ConversationRecovery.RecordFailure(intent, new SpacesUnavailable(stage, code, status));

        Assert.Equal("pending", intent.State);
        Assert.Equal("reauthorization-required", intent.Detail);
        Assert.Equal(originalId, intent.Id);
        Assert.Equal("original-operation", intent.OperationId);
        Assert.Equal(originalRecord, intent.RecordKey);
        Assert.Same(originalContent, intent.Content);
        Assert.False(ConversationRecovery.CanAttempt(intent, background: true));
        Assert.True(ConversationRecovery.CanAttempt(intent, background: false));
    }

    [Theory]
    [InlineData("write", "UpstreamFailure", 503)]
    [InlineData("write", "RateLimitExceeded", 429)]
    [InlineData("write", "Forbidden", 403)]
    [InlineData("write", "InvalidRecord", 400)]
    [InlineData("credential", "AuthenticationRequired", 401)]
    [InlineData("read-repo", "ExpiredToken", 401)]
    [InlineData("list-repos", "InvalidToken", 401)]
    public void Source_outages_and_fresh_Space_credential_errors_do_not_claim_the_OAuth_session_needs_renewal(string stage, string code, int status)
    {
        var failure = new SpacesUnavailable(stage, code, status);
        Assert.False(ConversationRecovery.RequiresReauthorization(failure));
        var intent = Intent();
        ConversationRecovery.RecordFailure(intent, failure);
        Assert.Equal("pending", intent.State);
        Assert.Equal("source-unavailable-retry-same-operation", intent.Detail);
        Assert.True(ConversationRecovery.CanAttempt(intent, background: true));
    }

    [Fact]
    public void An_explicit_retry_that_encounters_a_network_failure_can_reconcile_in_background_again()
    {
        var intent = Intent();
        ConversationRecovery.RecordFailure(intent, new SpacesUnavailable("write", "ScopeMissingError", 403));
        Assert.False(ConversationRecovery.CanAttempt(intent, background: true));
        ConversationRecovery.RecordFailure(intent, new HttpRequestException("An uncertain source result"));
        Assert.True(ConversationRecovery.CanAttempt(intent, background: true));
        Assert.Equal("original-operation", intent.OperationId);
        Assert.Equal("Keep this original text", intent.Content.Text);
    }

    [Fact]
    public void A_different_source_record_is_still_a_terminal_conflict()
    {
        var intent = Intent();
        ConversationRecovery.RecordFailure(intent, new WriteConflict());
        Assert.Equal("conflict", intent.State);
        Assert.Equal("source-key-has-different-content", intent.Detail);
        Assert.False(ConversationRecovery.CanAttempt(intent, background: true));
        Assert.False(ConversationRecovery.CanAttempt(intent, background: false));
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("conflict")]
    public void Terminal_receipts_do_not_start_another_source_write(string state)
    {
        var intent = Intent();
        intent.State = state;
        Assert.False(ConversationRecovery.CanAttempt(intent, background: false));
        Assert.False(ConversationRecovery.CanAttempt(intent, background: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reading_saved_outbound_messages_requires_authentication_and_the_post_grant(bool authenticatedReadOnly)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        if (authenticatedReadOnly)
        {
            var (credential, _) = ParticipantCredential.Issue(Did, "reader", 1, [ParticipationGrants.Read], DateTimeOffset.UtcNow);
            principal = ParticipationCredentials.Principal(credential);
        }
        // No service is supplied: rejection must happen before any private-intent lookup.
        var context = new DefaultHttpContext { User = principal };
        var controller = new ConversationController(null!) { ControllerContext = new ControllerContext { HttpContext = context } };
        var result = Assert.IsType<ObjectResult>(await controller.PendingMessages("workshop", TestContext.Current.CancellationToken));
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    private static WriteIntent Intent()
    {
        var id = WriteIntent.Key(Did, "workshop", "original-operation");
        return new WriteIntent
        {
            Id = id, AuthorDid = Did, RoomKey = "workshop", OperationId = "original-operation", RecordKey = "op-" + id,
            SpaceUri = "at://did:plc:authority/space/local.tangent.room/workshop",
            Content = new MessageContent("Keep this original text", DateTimeOffset.UnixEpoch, new SourceReference("original-source-uri", "original-cid"))
        };
    }
}
