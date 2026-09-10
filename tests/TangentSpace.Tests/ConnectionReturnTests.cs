using System.Security.Claims;
using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TangentSpace.AtProtocol;
using Xunit;

namespace TangentSpace.Tests;

public sealed class ConnectionReturnTests
{
    private const string Did = "did:plc:5rqf45qvouvadvz26a4m4al3";

    private static ConnectionsController Controller(string? did = Did)
    {
        var context = new DefaultHttpContext();
        if (did is not null) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AtprotoClaimTypes.Did, did)], "test"));
        return new ConnectionsController(Options.Create(new SpacesOptions()))
        { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("tangent-lounge", "/?room=tangent-lounge")]
    public void ConnectionReturnsToTheRequestedRoomWithoutChangingIdentity(string? room, string expected)
    {
        var controller = Controller();
        controller.Request.QueryString = new QueryString("?identifier=did:plc:someoneelse&scope=untrusted");
        var challenge = Assert.IsType<ChallengeResult>(controller.Rooms(room));
        Assert.Equal(expected, challenge.Properties!.RedirectUri);
        Assert.Equal(Did, controller.Request.Query["identifier"].ToString());
        Assert.False(controller.Request.Query.ContainsKey("scope"));
    }

    [Theory]
    [InlineData("//elsewhere.test")]
    [InlineData("https://elsewhere.test")]
    [InlineData("lounge&return=elsewhere")]
    [InlineData("../lounge")]
    [InlineData("")]
    public void RoomReturnCannotBecomeAnExternalRedirect(string room)
        => Assert.IsType<BadRequestObjectResult>(Controller().Rooms(room));

    [Fact]
    public void ConnectionRequiresHumanCookieIdentity()
    {
        Assert.IsType<UnauthorizedResult>(Controller(null).Rooms("tangent-lounge"));
        var controller = Controller();
        controller.Request.Headers.Authorization = "Bearer unused";
        Assert.IsType<UnauthorizedResult>(controller.Rooms("tangent-lounge"));
    }
}
