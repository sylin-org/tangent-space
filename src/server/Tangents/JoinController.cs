using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>Joining and leaving a private Tangent: <c>POST /t/{tangent}/join</c> sends <see cref="JoinTangent"/>, which
/// lets the participant in or records their ask, and <c>POST /t/{tangent}/leave</c> sends <see cref="LeaveTangent"/>. Both
/// judge for themselves, off the Doorway, since someone outside a private Tangent does not view it: a visitor is told to
/// sign in, and the commands judge the rest. A form is sent on, to the Tangent once they are in and to the Tangents
/// otherwise, and a JSON request reads where they stand (<see cref="Standing"/>).</summary>
public sealed class JoinController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string JoinSegment = "join";
    public const string LeaveSegment = "leave";
    public const string JoinRoute = "/t/{tangent}/" + JoinSegment;
    public const string LeaveRoute = "/t/{tangent}/" + LeaveSegment;

    /// <summary>Where a participant stands with a Tangent, as a JSON answer says it: its slug, its address, and
    /// <c>in</c>, <c>asked</c>, <c>declined</c> or <c>out</c>.</summary>
    public sealed record Standing(string Slug, string Address, string State);

    [HttpPost(JoinRoute)]
    [Tool(Name = "join_tangent", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Join a members-only Tangent read_tangents lists with joining join, or ask to join one with joining ask, which whoever manages it decides; your door tells you. Answers in, asked or declined.")]
    public async Task<IActionResult> Join([FromRoute, Description(Holds.Tangent)] string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (PostWrites.PersonId(User) is not { } personId) return this.Refused(Capabilities.SignIn);
        var outcome = await new JoinTangent(tangent, personId).Send();
        return outcome is Outcome<Joined>.Done { Result: var joined }
            ? Sent(tangent, joined.State)
            : outcome is Outcome<Joined>.Refused { Reason: var reason } ? this.Refused(reason) : Answers.NotDone(HttpContext, outcome);
    }

    [HttpPost(LeaveRoute)]
    [Tool(Name = "leave_tangent", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Leave a members-only Tangent you joined, or withdraw your waiting ask to join it. Your Posts stay; you may no longer read it.")]
    public async Task<IActionResult> Leave([FromRoute, Description(Holds.Tangent)] string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (PostWrites.PersonId(User) is not { } personId) return this.Refused(Capabilities.SignIn);
        var outcome = await new LeaveTangent(tangent, personId).Send();
        return outcome is Outcome<Tangent>.Done
            ? Sent(tangent, "out")
            : outcome is Outcome<Tangent>.Refused { Reason: var reason } ? this.Refused(reason) : Answers.NotDone(HttpContext, outcome);
    }

    private IActionResult Sent(string slug, string state)
    {
        var baseAddress = baseUrl.Resolve(HttpContext);
        var address = PostWrites.TangentAddress(baseAddress, slug);
        if (PostWrites.WantsJson(Request)) return Ok(new Standing(slug, address, state));
        Response.Headers.Location = state == Joined.InState ? address : baseAddress + TangentsController.Route;
        return StatusCode(StatusCodes.Status303SeeOther);
    }
}
