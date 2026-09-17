using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Identity;
using Tangent.Spaces;

namespace Tangent.Api;

/// <summary>Who is on this server. Until now nothing could answer that: every listing took the
/// ids it already held, and the only source of ids was a role's member array, so a participant
/// holding no role was invisible although their arrival was recorded (N-062). This reads the
/// participant spine itself, which is the one place that knows everyone.</summary>
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

    [HttpGet("")]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        // A people directory names everyone who has arrived, so it is the owner's to read.
        if (!await SpaceOwnership.IsOwner(User, ct)) return StatusCode(403);
        if (page is < 1 or > MaximumPage) return BadRequest(new { error = "Ask for a page between 1 and " + MaximumPage + "." });
        using var fresh = EntityContext.NoCache();
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
                Roles = bag.Tokens.Where(token => token.StartsWith("role:", StringComparison.Ordinal))
                    .Select(token => token["role:".Length..]).ToArray()
            });
        }
        return Ok(new { people, page, nextPage = result.HasNextPage ? page + 1 : (int?)null, total = result.TotalCount });
    }
}
