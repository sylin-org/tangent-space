using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Participation;

namespace TangentSpace.Activity;

[ApiController, Authorize, Route("api/activity")]
public sealed class ActivityController(TangentServer hub) : ControllerBase
{
    private ActivityService activity => hub.Activity;
    private LiveSessions live => hub.Live;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public Task<IActionResult> Overview([FromQuery] string? cursor, [FromQuery] string? channelCursor, CancellationToken ct)
        => Execute(async (did, credential) =>
        {
            var snapshot = await activity.Snapshot(did, credential, cursor, channelCursor, ct);
            await activity.EnsureSnapshotCurrent(did, credential, snapshot, ct);
            return Ok(snapshot);
        });

    [HttpGet("wait")]
    public Task<IActionResult> Wait([FromQuery] string? cursor, [FromQuery] string? channelCursor, CancellationToken ct)
        => Execute(async (did, credential) =>
        {
            var snapshot = await activity.Wait(did, credential, cursor, channelCursor, ct);
            await activity.EnsureSnapshotCurrent(did, credential, snapshot, ct);
            return Ok(snapshot);
        });

    [HttpGet("events")]
    public async Task Events([FromQuery] string? cursor, [FromQuery] string? channelCursor, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var did = RequireActor();
            var credential = User.FindFirst(ParticipationConstants.CredentialClaim)?.Value;
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "text/event-stream";
            Response.Headers["X-Accel-Buffering"] = "no";
            var current = await activity.Snapshot(did, credential, cursor, channelCursor, ct);
            await activity.EnsureSnapshotCurrent(did, credential, current, ct);
            await WriteEvent(current.ResetRequired ? "reset" : "activity", current, ct);
            // Token-only sessions: the connection authenticates once at open, then registers
            // under its session claim so a later sign-in that replaces this session can push
            // an identity_changed event through it (docs/DECISIONS.md, 11 September 2026).
            var session = User.FindFirst(ParticipationConstants.SessionClaim)?.Value;
            var wake = new TaskCompletionSource<LiveSessions.Identity>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registered = session is null ? null : live.Register(session, wake);
            var checkpoint = current.Checkpoint;
            while (!ct.IsCancellationRequested)
            {
                var wait = activity.Wait(did, credential, checkpoint, channelCursor, ct);
                if (wake.Task.IsCompleted || await Task.WhenAny(wait, wake.Task) != wait)
                {
                    // The browser signed in as someone else: deliver the event and close so
                    // the page re-fetches its identity and reconnects under the new session.
                    await Response.WriteAsync("event: identity_changed\ndata: "
                        + JsonSerializer.Serialize(await wake.Task, Json) + "\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                    return;
                }
                var next = await wait;
                if (next.ResetRequired || !string.Equals(next.Checkpoint, checkpoint, StringComparison.Ordinal))
                {
                    await activity.EnsureSnapshotCurrent(did, credential, next, ct);
                    await WriteEvent(next.ResetRequired ? "reset" : "activity", next, ct);
                    checkpoint = next.Checkpoint;
                }
                else
                {
                    await Response.WriteAsync(": keepalive\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (UnauthorizedAccessException) when (!Response.HasStarted)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            await Response.WriteAsJsonAsync(new { reason = "Your current access does not permit this operation." }, ct);
        }
        // If authority changes after streaming starts, closing the stream prevents further protected delivery.
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException error) when (!Response.HasStarted)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { reason = error.Message }, ct);
        }
    }

    private async Task WriteEvent(string name, ActivitySnapshot snapshot, CancellationToken ct)
    {
        await Response.WriteAsync("id: " + snapshot.Checkpoint + "\n", ct);
        await Response.WriteAsync("event: " + name + "\n", ct);
        await Response.WriteAsync("data: " + JsonSerializer.Serialize(snapshot, Json) + "\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private Task<IActionResult> Execute(Func<string, string?, Task<IActionResult>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        return Run();
        async Task<IActionResult> Run()
        {
            try
            {
                var did = RequireActor();
                return await operation(did, User.FindFirst(ParticipationConstants.CredentialClaim)?.Value);
            }
            catch (UnauthorizedAccessException) { return StatusCode(403, new { reason = "Your current access does not permit this operation." }); }
            catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
        }
    }

    private string RequireActor() => ParticipationAccess.Require(User, ParticipationGrants.Read);
}
