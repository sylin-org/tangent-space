using Koan.Data.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TangentSpace.Activity;
using TangentSpace.AtProtocol;
using TangentSpace.Communities;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Site;
using TangentSpace.Web;

namespace TangentSpace.Participation;

/// <summary>One compact, current-identity return packet for native WebMCP and unattended clients.</summary>
[ApiController, Route("api/participation/arrival")]
public sealed class ParticipantArrivalController(TangentGovernance tangents, ActivityService activity,
    SourceReadiness readiness, IOptions<SiteOptions> options) : ControllerBase
{
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? cursor, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            using var fresh = EntityContext.NoCache();
            var did = User.Identity?.IsAuthenticated == true ? ParticipationAccess.Require(User, ParticipationGrants.Welcome) : null;
            if (did is null && Request.Headers.ContainsKey("Authorization")) return Unauthorized();
            var expected = Request.Headers["X-Tangent-Participant"];
            if (expected.Count > 0 && (expected.Count != 1 || expected[0] != did))
                return Conflict(new { reason = "Your connected account changed. Reconnect before continuing." });
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            var participant = did is null ? null : await Participant.Get(did, ct);
            var credential = User.FindFirst(ParticipationConstants.CredentialClaim)?.Value;
            if (did is not null) await activity.EnsureParticipantActive(did, credential, ct);
            var canRead = did is not null && (!ParticipationAccess.UsesCredential(User)
                || User.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Read));
            var canPost = did is not null && (!ParticipationAccess.UsesCredential(User)
                || User.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Post));
            var directory = canRead ? await tangents.List(did, 1, ct) : null;
            var snapshot = canRead ? await activity.Snapshot(did!, credential, cursor, ct) : null;
            var source = did is null ? null : await readiness.Get(did, ct: ct);
            if (snapshot is not null) await activity.EnsureSnapshotCurrent(did!, credential, snapshot, ct);
            if (directory is not null)
                foreach (var tangent in directory.Tangents)
                    if (!await tangents.CanAccess(did!, tangent.Key, ct)) throw new UnauthorizedAccessException();
            return Ok(new
            {
                identity = participant is null ? null : new ParticipantWelcome(participant.Id, participant.Handle,
                    site?.IsOwner(participant.Id) == true, participant.JoinedAt),
                site = new { name = site?.Name ?? options.Value.Name, established = site is not null },
                tangents = directory, activity = snapshot, source,
                capabilities = new { read = canRead, post = canPost, activityWaitSeconds = 15,
                    independentActivityCursors = true, explicitReadAcknowledgement = true },
                actions = new { signIn = TangentConstants.SignInPath, connectAgent = "/agent.html",
                    readChannel = "/api/rooms/{roomKey}/messages", catchUp = "/api/activity", wait = "/api/activity/wait" }
            });
        }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { reason = "Your current access does not permit this operation." }); }
        catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
    }
}
