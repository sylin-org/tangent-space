using Koan.Data.Core;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Spaces;

namespace Tangent.Access;

/// <summary>Browser helpers for the server-role editor: CSRF intent, member search, and participant role assignment.</summary>
[ApiController]
[Authorize]
[Route("api/roles/ui")]
public sealed class RoleUiController(IAntiforgery antiforgery, ParticipantDirectory directory,
    ParticipantProfiles profiles, RoleCollection roles) : ControllerBase
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
        if (!await SpaceOwnership.IsOwner(User, ct)) return Forbid();
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

    [HttpGet("search")]
    public async Task<ActionResult<object>> Search([FromQuery] string role, [FromQuery] string? query, CancellationToken ct)
    {
        if (!await SpaceOwnership.IsOwner(User, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(role)) return BadRequest(new { error = "Specify a role to search." });

        var roleKey = role.StartsWith("role:", StringComparison.Ordinal) ? role : $"role:{role}";
        var targetRole = await roles.Get(roleKey, ct);
        if (targetRole is null) return NotFound(new { error = "Role not found." });

        var memberIds = targetRole.Members.ToHashSet(StringComparer.Ordinal);

        using var fresh = EntityContext.NoCache();
        var allParticipants = await Participant.All(ct);

        var list = new List<(Participant Participant, ParticipantProfile Profile, string? Did, bool InRole)>(allParticipants.Count);
        foreach (var p in allParticipants)
        {
            var profile = await profiles.Read(p.Id, ct);
            var did = await directory.AtprotoDidOf(p.Id, ct);
            var inRole = memberIds.Contains(p.Id);
            list.Add((p, profile, did, inRole));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            var membersOnly = list
                .Where(x => x.InRole)
                .OrderBy(x => x.Profile.DisplayName ?? x.Profile.Handle ?? x.Participant.Id, StringComparer.OrdinalIgnoreCase)
                .Select(x => new
                {
                    id = x.Participant.Id,
                    displayName = x.Profile.DisplayName,
                    handle = x.Profile.Handle,
                    avatar = x.Profile.Avatar,
                    label = x.Profile.DisplayName ?? x.Profile.Handle ?? "Participant",
                    did = x.Did,
                    inRole = true
                });
            return Ok(membersOnly);
        }

        var q = query.Trim();
        var matches = list.Where(x =>
            (x.Profile.DisplayName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (x.Profile.Handle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (x.Did?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            x.Participant.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
        );

        var sorted = matches
            .OrderByDescending(x => x.InRole)
            .ThenBy(x => x.Profile.DisplayName ?? x.Profile.Handle ?? x.Participant.Id, StringComparer.OrdinalIgnoreCase)
            .Take(ParticipantWindow)
            .Select(x => new
            {
                id = x.Participant.Id,
                displayName = x.Profile.DisplayName,
                handle = x.Profile.Handle,
                avatar = x.Profile.Avatar,
                label = x.Profile.DisplayName ?? x.Profile.Handle ?? "Participant",
                did = x.Did,
                inRole = x.InRole
            });

        return Ok(sorted);
    }

    [HttpGet("participant/{id}/roles")]
    public async Task<ActionResult<object>> ParticipantRoles(string id, CancellationToken ct)
    {
        if (!Participant.IsValidId(id)) return BadRequest(new { error = "Invalid participant identifier." });

        using var fresh = EntityContext.NoCache();
        var participant = await Participant.Get(id, ct);
        if (participant is null) return NotFound(new { error = "Participant not found." });

        var currentParticipantId = User.FindFirst(ParticipationConstants.ParticipantClaim)?.Value;
        var isSelf = !string.IsNullOrEmpty(currentParticipantId) && currentParticipantId == id;
        var isOwner = await SpaceOwnership.IsOwner(User, ct);

        var allRoles = await Koan.Identity.Roles.Role.All(ct);
        var roleList = allRoles
            .OrderBy(r => r.Id == TangentBuiltInRoles.Owner.Token ? 0 : 1)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var isSelfEnrollable = r.Metadata.TryGetValue("selfEnrollable", out var se) && se == "true";
                var isProtected = r.Id == TangentBuiltInRoles.Owner.Token;
                var isMember = r.Members.Contains(id);
                var canToggle = isOwner ? !isProtected : (isSelf && isSelfEnrollable);
                return new
                {
                    id = r.Id,
                    name = r.Name,
                    color = r.Metadata.TryGetValue("color", out var c) ? c : "#a98df0",
                    purpose = r.Metadata.TryGetValue("purpose", out var p) ? p : "",
                    isMember,
                    isSelfEnrollable,
                    isProtected,
                    canToggle
                };
            })
            .Where(r => isOwner || r.canToggle || r.isMember);

        return Ok(roleList);
    }

    [HttpPut("participant/{id}/roles/{roleId}")]
    public async Task<IActionResult> AddRole(string id, string roleId, CancellationToken ct)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest(new { error = "Antiforgery validation failed." });
        }

        if (!Participant.IsValidId(id)) return BadRequest(new { error = "Invalid participant identifier." });

        var currentParticipantId = User.FindFirst(ParticipationConstants.ParticipantClaim)?.Value;
        var isSelf = !string.IsNullOrEmpty(currentParticipantId) && currentParticipantId == id;
        var isOwner = await SpaceOwnership.IsOwner(User, ct);
        if (!isOwner && !isSelf) return Forbid();

        var role = await roles.Get(roleId, ct);
        if (role is null) return NotFound(new { error = "Role not found." });

        if (role.Id == TangentBuiltInRoles.Owner.Token)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Owner role cannot be granted here." });

        var isSelfEnrollable = role.Metadata.TryGetValue("selfEnrollable", out var se) && se == "true";
        if (!isOwner && (!isSelf || !isSelfEnrollable))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This role does not permit self-enrollment." });

        await roles.Add(role.Id, id, ct);
        return Ok(new { success = true, isMember = true });
    }

    [HttpDelete("participant/{id}/roles/{roleId}")]
    public async Task<IActionResult> RemoveRole(string id, string roleId, CancellationToken ct)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest(new { error = "Antiforgery validation failed." });
        }

        if (!Participant.IsValidId(id)) return BadRequest(new { error = "Invalid participant identifier." });

        var currentParticipantId = User.FindFirst(ParticipationConstants.ParticipantClaim)?.Value;
        var isSelf = !string.IsNullOrEmpty(currentParticipantId) && currentParticipantId == id;
        var isOwner = await SpaceOwnership.IsOwner(User, ct);
        if (!isOwner && !isSelf) return Forbid();

        var role = await roles.Get(roleId, ct);
        if (role is null) return NotFound(new { error = "Role not found." });

        if (role.Id == TangentBuiltInRoles.Owner.Token)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Owner role cannot be removed here." });

        var isSelfEnrollable = role.Metadata.TryGetValue("selfEnrollable", out var se) && se == "true";
        if (!isOwner && (!isSelf || !isSelfEnrollable))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This role does not permit self-enrollment." });

        await roles.Remove(role.Id, id, ct);
        return Ok(new { success = true, isMember = false });
    }
}
