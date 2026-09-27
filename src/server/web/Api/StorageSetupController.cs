using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Identity;
using Tangent.Infrastructure;

namespace Tangent.Api;

[ApiController, Authorize]
public sealed class StorageSetupController(StorageSetup storage) : ControllerBase
{
    [HttpGet("/api/server/storage")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await storage.Describe(User, ct)); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
    }

    [HttpPost("/api/server/storage/test"), TopicMutation(ParticipationGrants.Read)]
    public async Task<IActionResult> Test([FromBody] StorageTestRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!SameAccount(request.ExpectedDid)) return StatusCode(403);
        try { return Ok(await storage.Test(User, request.Provider, request.Settings, ct)); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
    }

    [HttpPost("/api/server/storage/activate"), TopicMutation(ParticipationGrants.Read)]
    public async Task<IActionResult> Activate([FromBody] StorageActivateRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!SameAccount(request.ExpectedDid)) return StatusCode(403);
        try { return Ok(await storage.Activate(User, request.TestId, ct)); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
    }

    private bool SameAccount(string? expectedDid)
        => !string.IsNullOrWhiteSpace(expectedDid)
            && string.Equals(expectedDid, User.FindFirst(AtprotoClaimTypes.Did)?.Value, StringComparison.Ordinal);
}

public sealed record StorageTestRequest(string Provider, IReadOnlyDictionary<string, string?> Settings,
    string? ExpectedDid = null);
public sealed record StorageActivateRequest(string TestId, string? ExpectedDid = null);
