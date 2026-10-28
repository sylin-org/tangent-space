using Koan.Data.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Community;
using Tangent.Identity;
using Tangent.Infrastructure;
using Tangent.Spaces;
using Tangent.Stewardship;

namespace Tangent.Api;

[ApiController, Authorize, Route("api/v1/tangents/{tangent}/joining")]
public sealed class TangentAdmissionController(TangentServer hub, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> State(string tangent, CancellationToken ct) => Execute(async actor =>
    {
        var description = await hub.Tangents.Describe(actor, tangent, ct);
        if (description is null) return NotFound();
        var membership = await TangentMembership.Get(TangentMembership.Key(tangent, actor), ct);
        var request = await TangentJoinRequest.Get(TangentJoinRequest.Key(tangent, actor), ct);
        var restriction = await Restrictions.ForTangent(actor, tangent, clock.GetUtcNow(), ct);
        var spaceAdmitted = await SpaceAdmission.Admitted(await Space.Get(TangentConstants.SpaceId, ct), actor, ct);
        var member = description.IsMember;
        var status = member ? "member" : membership?.Role == TangentRole.Removed ? "removed"
            : restriction is not null ? "restricted" : request?.Pending == true ? "pending"
            : request?.Accepted == false ? "refused" : "visitor";
        return Ok(new { status, admission = description.Admission.ToString(),
            canJoin = spaceAdmitted && status == "visitor", canLeave = member && !description.IsOwner,
            canReview = await hub.Participants.CanAdminister(actor, tangent, null, ct) });
    }, ParticipationGrants.Read);

    [HttpPost, TopicMutation(ParticipationGrants.Read)]
    public Task<IActionResult> Join(string tangent, CancellationToken ct) => Execute(async actor =>
    {
        if (await hub.Tangents.Describe(actor, tangent, ct) is null) return NotFound();
        var result = await hub.Participants.Join(actor, tangent, ct);
        return Ok(new { status = result.Outcome == JoinOutcome.PendingApproval ? "pending" : "member" });
    }, ParticipationGrants.Read);

    [HttpDelete, TopicMutation(ParticipationGrants.Read)]
    public Task<IActionResult> Leave(string tangent, CancellationToken ct) => Execute(async actor =>
    {
        await hub.Participants.Leave(actor, tangent, ct);
        return Ok(new { status = "visitor" });
    }, ParticipationGrants.Read);

    [HttpGet("requests")]
    public Task<IActionResult> Requests(string tangent, [FromQuery] int page = 1, CancellationToken ct = default) => Execute(async actor =>
    {
        var rows = await hub.Participants.PendingJoinRequests(actor, tangent, page, ct);
        var items = new List<object>();
        foreach (var row in rows)
        {
            var profile = await hub.Profiles.Read(row.ParticipantId, ct);
            var person = await Participant.Get(row.ParticipantId, ct);
            items.Add(new { id = row.ParticipantId, displayName = profile.DisplayName ?? profile.Handle ?? "Participant",
                profile.Handle, profile.Avatar, classification = person?.Classification.ToString(), row.RequestedAt });
        }
        return Ok(new { items, nextPage = rows.Count == 25 ? (int?)(page + 1) : null });
    });

    [HttpPost("requests/{participant}"), TopicMutation]
    public Task<IActionResult> Decide(string tangent, string participant, TangentAdmissionDecision input, CancellationToken ct)
        => Execute(async actor =>
        {
            var result = await hub.Participants.DecideJoinRequest(actor, TangentJoinRequest.Key(tangent, participant), input.Accept, ct);
            return Ok(new { accepted = result.Accepted });
        });

    private async Task<IActionResult> Execute(Func<string, Task<IActionResult>> action, string grant = ParticipationGrants.Manage)
    {
        Response.Headers.CacheControl = "no-store";
        try { using var fresh = EntityContext.NoCache(); return await action(ParticipationAccess.Require(User, grant)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
        catch (TangentRuleViolation error) { return StatusCode(error.Denial == TangentDenial.NotFound ? 404 : error.Denial == TangentDenial.Forbidden ? 403 : 400, new { message = error.Message }); }
    }
}

public sealed record TangentAdmissionDecision(bool Accept);
