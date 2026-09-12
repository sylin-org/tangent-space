using System.Text.Json;
using System.Threading.Channels;
using CarpaNet.Identity;
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
            return Ok(snapshot with { ParticipantRef = did });
        });

    [HttpGet("wait")]
    public Task<IActionResult> Wait([FromQuery] string? cursor, [FromQuery] string? channelCursor, CancellationToken ct)
        => Execute(async (did, credential) =>
        {
            var snapshot = await activity.Wait(did, credential, cursor, channelCursor, ct);
            await activity.EnsureSnapshotCurrent(did, credential, snapshot, ct);
            return Ok(snapshot with { ParticipantRef = did });
        });

    [HttpGet("events")]
    public async Task Events([FromQuery] string? cursor, [FromQuery] string? channelCursor,
        [FromQuery] string[]? profileDid, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var did = RequireActor();
            var credential = User.FindFirst(ParticipationConstants.CredentialClaim)?.Value;
            profileDid ??= [];
            if (profileDid.Length > 64 || profileDid.Any(value => !IdentityResolver.IsValidDid(value)))
                throw new ArgumentException("Profile subscriptions must contain at most 64 valid DIDs.", nameof(profileDid));
            var watchedProfiles = profileDid.ToHashSet(StringComparer.Ordinal);
            var profileChanges = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
                { FullMode = BoundedChannelFullMode.DropWrite });
            using var profileSubscription = watchedProfiles.Count == 0 ? null : hub.Profiles.Subscribe(changed =>
            {
                if (watchedProfiles.Contains(changed)) profileChanges.Writer.TryWrite(true);
            });
            var deliveredProfiles = new Dictionary<string, string>(StringComparer.Ordinal);
            // Register before the first asynchronous snapshot/query/write so a replacing
            // sign-in during that work cannot fall between initial delivery and registration.
            var session = User.FindFirst(ParticipationConstants.SessionClaim)?.Value;
            var wake = new TaskCompletionSource<LiveSessions.Identity>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registered = session is null ? null : live.Register(session, wake);
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "text/event-stream";
            Response.Headers["X-Accel-Buffering"] = "no";
            var current = await activity.Snapshot(did, credential, cursor, channelCursor, ct);
            await activity.EnsureSnapshotCurrent(did, credential, current, ct);
            if (wake.Task.IsCompleted) { await WriteIdentity(await wake.Task, ct); return; }
            await WriteEvent(current.ResetRequired ? "reset" : "activity", current with { ParticipantRef = did }, ct);
            await WriteProfiles(watchedProfiles, deliveredProfiles, ct);
            var checkpoint = current.Checkpoint;
            while (!ct.IsCancellationRequested)
            {
                if (wake.Task.IsCompleted) { await WriteIdentity(await wake.Task, ct); return; }
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var wait = activity.Wait(did, credential, checkpoint, channelCursor, waitCancellation.Token);
                var profileWake = watchedProfiles.Count == 0 ? null : profileChanges.Reader.WaitToReadAsync(ct).AsTask();
                try
                {
                    var completed = profileWake is null
                        ? await Task.WhenAny(wait, wake.Task)
                        : await Task.WhenAny(wait, wake.Task, profileWake);
                    if (wake.Task.IsCompleted || completed == wake.Task)
                    {
                        // Flush identity before cleanup; a provider that ignores cancellation
                        // must not delay the browser's identity transition until its wait completes.
                        await WriteIdentity(await wake.Task, ct);
                        return;
                    }
                    if (completed == profileWake)
                    {
                        await profileWake!;
                        while (profileChanges.Reader.TryRead(out _)) { }
                        if (wake.Task.IsCompleted) { await WriteIdentity(await wake.Task, ct); return; }
                        await WriteProfiles(watchedProfiles, deliveredProfiles, ct);
                        continue;
                    }
                    var next = await wait;
                    if (next.ResetRequired || !string.Equals(next.Checkpoint, checkpoint, StringComparison.Ordinal))
                    {
                        await activity.EnsureSnapshotCurrent(did, credential, next, ct);
                        if (wake.Task.IsCompleted) { await WriteIdentity(await wake.Task, ct); return; }
                        await WriteEvent(next.ResetRequired ? "reset" : "activity", next with { ParticipantRef = did }, ct);
                        checkpoint = next.Checkpoint;
                    }
                    else
                    {
                        await Response.WriteAsync(": keepalive\n\n", ct);
                        await Response.Body.FlushAsync(ct);
                    }
                }
                finally
                {
                    // A normally completed response does not cancel RequestAborted. Explicitly
                    // cancel this losing wait and observe any later provider fault without awaiting it.
                    _ = wait.ContinueWith(static completed => { _ = completed.Exception; },
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    waitCancellation.Cancel();
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

    private async Task WriteIdentity(LiveSessions.Identity identity, CancellationToken ct)
    {
        await Response.WriteAsync("event: identity_changed\ndata: " + JsonSerializer.Serialize(identity, Json) + "\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private async Task WriteEvent(string name, ActivitySnapshot snapshot, CancellationToken ct)
    {
        await Response.WriteAsync("id: " + snapshot.Checkpoint + "\n", ct);
        await Response.WriteAsync("event: " + name + "\n", ct);
        await Response.WriteAsync("data: " + JsonSerializer.Serialize(snapshot, Json) + "\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private async Task WriteProfiles(IReadOnlySet<string> watched, IDictionary<string, string> delivered,
        CancellationToken ct)
    {
        foreach (var identifier in watched)
        {
            var snapshot = await hub.Profiles.Cached(identifier, ct);
            if (snapshot.CapturedAt == default) continue;
            var payload = JsonSerializer.Serialize(snapshot.Present(), Json);
            if (delivered.TryGetValue(identifier, out var prior) && prior == payload) continue;
            await Response.WriteAsync("event: profile\ndata: " + payload + "\n\n", ct);
            delivered[identifier] = payload;
        }
        if (watched.Count > 0) await Response.Body.FlushAsync(ct);
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
