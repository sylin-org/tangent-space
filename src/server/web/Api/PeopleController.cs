using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Identity;
using Tangent.Spaces;

namespace Tangent.Api;

/// <summary>A bounded directory of verified arrivals for authorized participant stewards.</summary>
[ApiController, Authorize, Route("api/people")]
public sealed class PeopleController(ParticipantProfiles profiles, ParticipantDirectory directory,
    TangentRoleAccess roleAccess) : ControllerBase
{
    public const int PageSize = 50;
    public const int MaximumPage = 200;

    private static readonly QueryDefinition byArrival = new()
    {
        Sort = SortSpecParser.ParseStrict<Participant>(nameof(Participant.JoinedAt)),
        CountStrategy = CountStrategy.Exact
    };

    private async Task<bool> CanManage(CancellationToken ct)
    {
        string actor;
        try { actor = ParticipationAccess.Require(User, ParticipationGrants.Manage); }
        catch (UnauthorizedAccessException) { return false; }
        return await roleAccess.CanManageParticipants(actor, ct);
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery]string identifier,CancellationToken ct) {
        Response.Headers.CacheControl="no-store";using var fresh=EntityContext.NoCache();if(!await CanManage(ct))return StatusCode(403);
        if(string.IsNullOrWhiteSpace(identifier)||identifier.Length>253)return BadRequest();var found=await directory.ByIdentifier(identifier.Trim().TrimStart('@'),ct);if(found is null)return NotFound();var participant=found.Value.Participant;var profile=await profiles.Read(participant.Id,ct);var bag=await roleAccess.Bag(participant.Id,ct);
        return Ok(new {participant.Id,did=await directory.AtprotoDidOf(participant.Id,ct),profile.Handle,profile.Avatar,label=profile.DisplayName??profile.Handle??"Participant",participant.JoinedAt,participant.IsSuspended,classification=participant.Classification.ToString(),roles=bag.Where(token=>token.StartsWith("role:",StringComparison.Ordinal)).Select(token=>token[5..]).ToArray()});
    }
    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        using var fresh = EntityContext.NoCache();
        if (!await CanManage(ct)) return StatusCode(403);
        if (page is < 1 or > MaximumPage) return BadRequest(new { error = "Ask for a page between 1 and " + MaximumPage + "." });
        var result = await Participant.AllWithCount(byArrival.WithPagination(page, PageSize), ct);
        var people = new List<object>(result.Items.Count);
        foreach (var participant in result.Items)
        {
            var profile = await profiles.Read(participant.Id, ct);
            var bag = await roleAccess.Bag(participant.Id, ct);
            people.Add(new
            {
                participant.Id,
                Did = await directory.AtprotoDidOf(participant.Id, ct),
                profile.Handle,
                profile.DisplayName,
                profile.Avatar,
                Label = profile.DisplayName ?? profile.Handle ?? "Participant",
                participant.JoinedAt,
                participant.LastArrivedAt,
                participant.IsSuspended,
                Classification = participant.Classification.ToString(),
                // Only the granted roles: the bag also carries the ambient everyone/authenticated
                // tokens, and listing those beside a real grant would read as one.
                Roles = bag.Where(token => token.StartsWith("role:", StringComparison.Ordinal))
                    .Select(token => token["role:".Length..]).ToArray()
            });
        }
        return Ok(new { people, page, nextPage = result.HasNextPage ? page + 1 : (int?)null, total = result.TotalCount });
    }
}
