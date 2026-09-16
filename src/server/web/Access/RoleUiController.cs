using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Identity;
using Tangent.Spaces;

namespace Tangent.Access;

/// <summary>Browser helpers for the server-role editor: CSRF intent and bounded participant identity lookup.</summary>
[ApiController]
[Authorize]
[Route("api/roles/ui")]
public sealed class RoleUiController(IAntiforgery antiforgery, ParticipantDirectory directory,
    ParticipantProfiles profiles) : ControllerBase
{
    public const int ParticipantWindow = 50;

    [HttpGet("session")]
    public ActionResult<object> Session()
    {
        Response.Headers.CacheControl = "no-store";
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { tokens.RequestToken, tokens.HeaderName });
    }

    [HttpGet("descriptor")]
    public async Task<ActionResult<object>> Descriptor(CancellationToken ct)
    {
        if (!await IsOwner(ct)) return Forbid();
        return Ok(new
        {
            Capabilities = TangentPermissions.Catalog.Select(permission => new
            {
                Key = permission.Token,
                Label = permission.Name,
                permission.Description
            })
        });
    }

    [HttpGet("resolve")]
    public async Task<ActionResult<object>> Resolve([FromQuery] string identifier, CancellationToken ct)
    {
        if (!await IsOwner(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 256)
            return BadRequest(new { error = "Enter one handle, DID, or participant identifier." });
        var match = await directory.ByIdentifier(identifier, ct);
        var participant = match?.Participant;
        if (participant is null && Participant.IsValidId(identifier)) participant = await directory.ByInternal(identifier, ct);
        if (participant is null) return NotFound(new { error = "No participant is registered under that identity." });
        var profile = await profiles.Read(participant.Id, ct);
        var label = profile.DisplayName ?? profile.Handle ?? "Participant";
        return Ok(new
        {
            participant.Id,
            Label = label,
            Did = await directory.AtprotoDidOf(participant.Id, ct),
            profile.Handle,
            profile.DisplayName,
            profile.Avatar
        });
    }

    [HttpGet("participants")]
    public async Task<ActionResult<object>> Participants([FromQuery(Name = "id")] string[] ids, CancellationToken ct)
    {
        if (!await IsOwner(ct)) return Forbid();
        var window = ids.Where(Participant.IsValidId).Distinct(StringComparer.Ordinal).Take(ParticipantWindow + 1).ToArray();
        if (window.Length > ParticipantWindow) return BadRequest(new { error = $"Request at most {ParticipantWindow} participants." });
        var people = await Task.WhenAll(window.Select(async id =>
        {
            var profile = await profiles.Read(id, ct);
            return new
            {
                Id = id,
                Label = profile.DisplayName ?? profile.Handle ?? "Participant",
                profile.Handle,
                profile.DisplayName,
                profile.Avatar
            };
        }));
        return Ok(people);
    }

    private async Task<bool> IsOwner(CancellationToken ct)
    {
        string actor;
        try { actor = ParticipationAccess.Require(User, ParticipationGrants.Manage); }
        catch (UnauthorizedAccessException) { return false; }
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        return space?.IsOwner(actor) == true;
    }
}
