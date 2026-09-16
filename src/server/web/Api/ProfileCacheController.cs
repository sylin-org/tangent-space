using System.Text.Json;
using System.Threading.Channels;
using CarpaNet.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Identity;
using Tangent.Identity;

namespace Tangent.Api;

[ApiController]
public sealed class ProfileCacheController(ParticipantProfiles profiles, ParticipantDirectory directory,
    IWebHostEnvironment environment) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [AllowAnonymous, HttpGet("/api/profile-cache/avatar")]
    public async Task<IActionResult> Avatar([FromQuery] string did, [FromQuery] string? v, CancellationToken ct)
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        if (!IdentityResolver.IsValidDid(did)) return BadRequest();
        var cached = await profiles.Cached(did, ct);
        var versioned = v is not null;
        v ??= cached.AvatarFile;
        if (v is not null && ValidFile(v))
        {
            var path = Path.Combine(ProfileCapture.MediaDirectory(environment), v);
            if (System.IO.File.Exists(path))
            {
                Response.Headers.CacheControl = versioned ? "public,max-age=31536000,immutable" : "no-cache";
                return PhysicalFile(path, Path.GetExtension(v) == ".jpg" ? "image/jpeg" : "image/" + Path.GetExtension(v)[1..]);
            }
        }
        // Only enrolled identities may schedule retrieval. The proxy never forwards a request.
        if (await directory.ByDid(did, ct) is not null)
        {
            if (cached.RetryAfter <= DateTimeOffset.UtcNow || cached.AvatarFile is not null && cached.AvatarFile == v) profiles.Request(did);
        }
        Response.Headers.CacheControl = "no-store";
        return Content("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"80\" height=\"80\" viewBox=\"0 0 80 80\"><rect width=\"80\" height=\"80\" rx=\"40\" fill=\"#292332\"/><circle cx=\"40\" cy=\"30\" r=\"13\" fill=\"#bdb0ca\"/><path d=\"M16 72a24 24 0 0 1 48 0\" fill=\"#bdb0ca\"/></svg>", "image/svg+xml");
    }

    [Authorize, HttpGet("/api/profile-cache/events")]
    public async Task Events([FromQuery] string[] did, CancellationToken ct)
    {
        try { ParticipationAccess.Require(User, ParticipationGrants.Welcome); }
        catch (UnauthorizedAccessException) { Response.StatusCode = 403; return; }
        if (did.Length is 0 or > 64 || did.Any(value => !IdentityResolver.IsValidDid(value))) { Response.StatusCode = 400; return; }
        var watched = did.ToHashSet(StringComparer.Ordinal);
        // Coalesce changes; after a wake, read current snapshots for just this viewer's requested DIDs.
        var changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        using var subscription = profiles.Subscribe(changed => { if (watched.Contains(changed)) changes.Writer.TryWrite(true); });
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        var delivered = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            Task<bool>? pending = null;
            while (!ct.IsCancellationRequested)
            {
                foreach (var identifier in watched)
                {
                    var snapshot = await profiles.Cached(identifier, ct);
                    if (snapshot.CapturedAt == default) continue;
                    // Public AT decoration only: no local membership, roles, handles or credentials.
                    var payload = JsonSerializer.Serialize(snapshot.Present(), Json);
                    if (delivered.GetValueOrDefault(identifier) == payload) continue;
                    await Response.WriteAsync("event: profile\ndata: " + payload + "\n\n", ct);
                    delivered[identifier] = payload;
                }
                await Response.WriteAsync(": ready\n\n", ct);
                await Response.Body.FlushAsync(ct);
                pending ??= changes.Reader.WaitToReadAsync(ct).AsTask();
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var delay = Task.Delay(TimeSpan.FromSeconds(25), heartbeat.Token);
                if (await Task.WhenAny(pending, delay) == pending)
                {
                    heartbeat.Cancel();
                    await pending; pending = null;
                    while (changes.Reader.TryRead(out _)) { }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static bool ValidFile(string value)
    {
        var extension = Path.GetExtension(value);
        var hash = Path.GetFileNameWithoutExtension(value);
        return value == hash + extension && hash.Length == 64 && hash.All(char.IsAsciiHexDigitLower)
            && extension is ".png" or ".jpg" or ".webp" or ".gif";
    }
}
