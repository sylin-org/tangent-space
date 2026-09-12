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
public sealed class ParticipantArrivalController(TangentServer hub, IOptions<SiteOptions> options) : ControllerBase
{
    private TangentGovernance tangents => hub.Tangents;
    private ActivityService activity => hub.Activity;
    private SourceReadiness readiness => hub.Readiness;

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? cursor, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            using var fresh = EntityContext.NoCache();
            var participantId = User.Identity?.IsAuthenticated == true ? ParticipationAccess.Require(User, ParticipationGrants.Welcome) : null;
            if (participantId is null && Request.Headers.ContainsKey("Authorization")) return Unauthorized();
            var expected = Request.Headers["X-Tangent-Participant"];
            if (expected.Count > 0 && (expected.Count != 1 || expected[0] != participantId))
                return Conflict(new { reason = "Your connected account changed. Reconnect before continuing." });
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            var participant = participantId is null ? null : await Participant.Get(participantId, ct);
            var credential = User.FindFirst(ParticipationConstants.CredentialClaim)?.Value;
            if (participantId is not null) await activity.EnsureParticipantActive(participantId, credential, ct);
            var canRead = participantId is not null && (!ParticipationAccess.UsesCredential(User)
                || User.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Read));
            var canPost = participantId is not null && (!ParticipationAccess.UsesCredential(User)
                || User.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Post));
            var directory = canRead ? await tangents.List(participantId, 1, ct) : null;
            var snapshot = canRead ? await activity.Snapshot(participantId!, credential, cursor, ct) : null;
            var source = participantId is null ? null : await ReadinessOf(participantId, ct);
            if (snapshot is not null) await activity.EnsureSnapshotCurrent(participantId!, credential, snapshot, ct);
            if (directory is not null)
                foreach (var tangent in directory.Tangents)
                    if (!await tangents.CanAccess(participantId!, tangent.Key, ct)) throw new UnauthorizedAccessException();
            return Ok(new
            {
                identity = participant is null ? null : new ParticipantWelcome(participant.Id,
                    await hub.Directory.AtprotoDidOf(participant.Id, ct), await hub.Directory.LabelOf(participant.Id, ct),
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

    /// <summary>Source readiness is atproto-scoped: an internal-only participant has no source
    /// provider to connect, which reports as unsupported rather than pending.</summary>
    private async Task<object?> ReadinessOf(string participantId, CancellationToken ct)
    {
        var did = await hub.Directory.AtprotoDidOf(participantId, ct);
        return did is null ? null : await readiness.Get(did, ct: ct);
    }
}
