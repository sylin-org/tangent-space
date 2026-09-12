using System.Security.Cryptography;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto.Protocol;

namespace TangentSpace.Participants;

/// <summary>The only upstream profile reader. Persists media before announcing a new snapshot.</summary>
public sealed class ProfileCapture(ParticipantProfiles profiles, AtprotoSessions sessions, AtprotoHttp network,
    IWebHostEnvironment environment, TimeProvider clock, ILogger<ProfileCapture> logger) : BackgroundService
{
    public const int MaxImageBytes = 5 * 1024 * 1024;
    public static string MediaDirectory(IWebHostEnvironment environment) => Path.Combine(environment.ContentRootPath, "profile-media");

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Run(stoppingToken)));

    private async Task Run(CancellationToken ct)
    {
        await foreach (var did in profiles.Requests.ReadAllAsync(ct))
        {
            try
            {
                using var fresh = EntityContext.NoCache();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await Capture(did, timeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                logger.LogDebug("Public profile capture deferred ({FailureType}).", error.GetType().Name);
                try
                {
                    var old = await profiles.Cached(did, ct);
                    await profiles.Store(new() { Id = did, DisplayName = old.DisplayName, Description = old.Description,
                        AvatarFile = old.AvatarFile, AvatarCid = old.AvatarCid, CapturedAt = old.CapturedAt,
                        RetryAfter = clock.GetUtcNow().AddMinutes(2) }, false, ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested) { /* A later read will request capture again. */ }
            }
            finally { profiles.Complete(did); }
        }
    }

    private async Task Capture(string did, CancellationToken ct)
    {
        var old = await profiles.Cached(did, ct);
        var document = await sessions.ResolveDid(did, ct);
        var pds = document.PdsEndpoint?.TrimEnd('/') ?? throw new InvalidDataException();
        using var request = new HttpRequestMessage(HttpMethod.Get, pds + "/xrpc/com.atproto.repo.getRecord?repo="
            + Uri.EscapeDataString(did) + "&collection=app.bsky.actor.profile&rkey=self");
        using var response = await network.Send(request, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        if (root.GetProperty("uri").GetString() != "at://" + did + "/app.bsky.actor.profile/self") throw new InvalidDataException();
        var value = root.GetProperty("value");
        string? Text(string name, int limit) => value.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.String && field.GetString() is { } text ? text[..Math.Min(text.Length, limit)] : null;
        string? filename = null, cid = null;
        if (value.TryGetProperty("avatar", out var blob))
        {
            cid = blob.GetProperty("ref").GetProperty("$link").GetString();
            if (cid is not { Length: > 0 and < 200 } || !cid.All(char.IsAsciiLetterOrDigit)) throw new InvalidDataException();
            if (cid == old.AvatarCid && old.AvatarFile is not null && File.Exists(Path.Combine(MediaDirectory(environment), old.AvatarFile)))
                filename = old.AvatarFile;
            else
            {
                using var imageRequest = new HttpRequestMessage(HttpMethod.Get, pds + "/xrpc/com.atproto.sync.getBlob?did=" + Uri.EscapeDataString(did) + "&cid=" + cid);
                using var image = await network.Send(imageRequest, ct);
                image.EnsureSuccessStatusCode();
                if (image.Content.Headers.ContentLength > MaxImageBytes) throw new InvalidDataException();
                await using var stream = await image.Content.ReadAsStreamAsync(ct);
                using var bytes = new MemoryStream();
                var buffer = new byte[8192]; int count;
                while ((count = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    if (bytes.Length + count > MaxImageBytes) throw new InvalidDataException();
                    bytes.Write(buffer, 0, count);
                }
                var content = bytes.ToArray();
                var extension = ImageExtension(content) ?? throw new InvalidDataException();
                filename = Convert.ToHexStringLower(SHA256.HashData(content)) + extension;
                Directory.CreateDirectory(MediaDirectory(environment));
                var path = Path.Combine(MediaDirectory(environment), filename);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { await File.WriteAllBytesAsync(temporary, content, ct); File.Move(temporary, path, overwrite: true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        var snapshot = new ParticipantProfileSnapshot { Id = did, DisplayName = Text("displayName", 640),
            Description = Text("description", 2560), AvatarFile = filename, AvatarCid = cid,
            CapturedAt = clock.GetUtcNow(), RetryAfter = clock.GetUtcNow().AddHours(6) };
        var changed = old.CapturedAt == default || old.DisplayName != snapshot.DisplayName
            || old.Description != snapshot.Description || old.AvatarFile != snapshot.AvatarFile;
        await profiles.Store(snapshot, changed, ct);
    }

    internal static string? ImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) && bytes.AsSpan(12,4).SequenceEqual("IHDR"u8)) return ".png";
        if (bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return ".jpg";
        if (bytes.Length >= 16 && bytes.AsSpan(0,4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8,4).SequenceEqual("WEBP"u8)) return ".webp";
        if (bytes.Length >= 10 && (bytes.AsSpan(0,6).SequenceEqual("GIF87a"u8) || bytes.AsSpan(0,6).SequenceEqual("GIF89a"u8))) return ".gif";
        return null;
    }
}
