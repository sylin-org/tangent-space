using Koan.Data.Core;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Spaces;
using CookieAuthentication = Koan.Web.Auth.Extensions.AuthenticationExtensions;

namespace Tangent.Access;

/// <summary>Browser helpers for the server-role editor: CSRF intent, member search, and participant role assignment.</summary>
[ApiController]
[Authorize(AuthenticationSchemes = CookieAuthentication.CookieScheme)]
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
    public async Task<ActionResult<object>> Search([FromQuery] string role, [FromQuery] string? query,
        CancellationToken ct, [FromQuery] int page = 1)
    {
        if (!await SpaceOwnership.IsOwner(User, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(role)) return BadRequest(new { error = "Specify a role to search." });
        if (page is < 1 or > 10000) return BadRequest(new { error = "Choose a member page between 1 and 10000." });
        if (query?.Length > 253) return BadRequest(new { error = "Enter a handle or DID of at most 253 characters." });
        Response.Headers.CacheControl = "no-store";
        var roleKey = role.StartsWith("role:", StringComparison.Ordinal) ? role : $"role:{role}";
        var targetRole = await roles.Get(roleKey, ct);
        if (targetRole is null) return NotFound(new { error = "Role not found." });

        using var fresh = EntityContext.NoCache();
        var memberIds = targetRole.Members.ToHashSet(StringComparer.Ordinal);
        var selected = new List<Participant>(ParticipantWindow);
        int? nextPage = null;
        if (string.IsNullOrWhiteSpace(query))
        {
            // Koan owns the role's membership document. Bound profile hydration to this page;
            // membership size is not inferred from the number of hydrated profiles returned.
            var ids = memberIds.Order(StringComparer.Ordinal).Skip((page - 1) * ParticipantWindow)
                .Take(ParticipantWindow + 1).ToArray();
            nextPage = ids.Length > ParticipantWindow ? page + 1 : null;
            foreach (var id in ids.Take(ParticipantWindow))
                if (await Participant.Get(id, ct) is { } participant) selected.Add(participant);
        }
        else
        {
            try
            {
                if (await ParticipantLookup.TryResolveByIdentifier(query.Trim(), ct) is { } resolved)
                    selected.Add(resolved.Participant);
            }
            catch (ArgumentException invalid) { return BadRequest(new { error = invalid.Message }); }
        }
        var items = new List<object>(selected.Count);
        foreach (var participant in selected)
        {
            var profile = await profiles.Read(participant.Id, ct);
            items.Add(new
            {
                id = participant.Id, displayName = profile.DisplayName, handle = profile.Handle,
                avatar = profile.Avatar, label = profile.DisplayName ?? profile.Handle ?? "Participant",
                did = await directory.AtprotoDidOf(participant.Id, ct), inRole = memberIds.Contains(participant.Id)
            });
        }
        return Ok(new { items, page, nextPage, totalMembers = memberIds.Count });
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
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var participantIsOwner = space?.IsOwner(id) == true;

        var allRoles = await Koan.Identity.Roles.Role.All(ct);
        var roleList = allRoles
            .OrderBy(r => r.Id == TangentBuiltInRoles.Owner.Token ? 0 : 1)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var isProtected = TangentDerivedRoleGuard.IsDerived(r.Id);
                var isSelfEnrollable = !isProtected && r.Metadata.TryGetValue("selfEnrollable", out var se) && se == "true";
                var isMember = r.Id == TangentBuiltInRoles.Owner.Token ? participantIsOwner
                    : r.Id == TangentBuiltInRoles.Participant.Token || r.Id == "role:everyone" || r.Members.Contains(id);
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

        if (TangentDerivedRoleGuard.IsDerived(roleId))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Derived roles cannot be granted or removed." });

        var role = await roles.Get(roleId, ct);
        if (role is null) return NotFound(new { error = "Role not found." });

        if (TangentDerivedRoleGuard.IsDerived(role.Id))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Derived roles cannot be granted or removed." });

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

        if (TangentDerivedRoleGuard.IsDerived(roleId))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Derived roles cannot be granted or removed." });

        var role = await roles.Get(roleId, ct);
        if (role is null) return NotFound(new { error = "Role not found." });

        if (TangentDerivedRoleGuard.IsDerived(role.Id))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Derived roles cannot be granted or removed." });

        var isSelfEnrollable = role.Metadata.TryGetValue("selfEnrollable", out var se) && se == "true";
        if (!isOwner && (!isSelf || !isSelfEnrollable))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "This role does not permit self-enrollment." });

        await roles.Remove(role.Id, id, ct);
        return Ok(new { success = true, isMember = false });
    }
}
