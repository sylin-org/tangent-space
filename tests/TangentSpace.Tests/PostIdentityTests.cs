using System.Security.Claims;
using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using TangentSpace.Conversation;
using Xunit;

namespace TangentSpace.Tests;

public sealed class PostIdentityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_stale_browser_identity_cannot_send_or_read_private_intents_using_another_tabs_new_cookie(bool multipleValues, bool pendingLookup)
    {
        const string signedInDid = "did:plc:6c26drqwrk5yavtnnjuvpvdf";
        const string displayed = "did:plc:5rqf45qvouvadvz26a4m4al3";
        var signedIn = TangentSpace.Participants.Participant.NewIdentifier();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(AtprotoClaimTypes.Did, signedInDid), new Claim(TangentSpace.Participation.ParticipationConstants.ParticipantClaim, signedIn)], "cookie"))
        };
        context.Request.Headers["X-Tangent-Participant"] = multipleValues ? new StringValues([signedIn, displayed]) : displayed;
        // No service: reject the stale identity before a source operation or intent lookup.
        var controller = new ConversationController(null!) { ControllerContext = new ControllerContext { HttpContext = context } };
        var result = pendingLookup
            ? await controller.PendingMessages("tangent-lounge", TestContext.Current.CancellationToken)
            : await controller.Post("tangent-lounge", new PostMessage("unchanged-operation", "Keep my attribution", null), TestContext.Current.CancellationToken);
        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }
}
