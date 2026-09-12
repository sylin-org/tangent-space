using System.Text.Json;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Microsoft.Extensions.Caching.Memory;

namespace TangentSpace.Participants;

public sealed record ParticipantProfile(string Did, string? Handle, string? DisplayName,
    string? Description, string? Avatar, string Status);

/// <summary>Optional public profile decoration. The authenticated DID and verified handle
/// come from Tangent; profile text never determines identity or permissions.</summary>
public sealed class ParticipantProfiles(AtprotoSessions sessions, AtprotoHttp network, IMemoryCache cache,
    ParticipantDirectory directory)
{
    /// <summary>Profile decoration for an atproto identity: reads the atproto repo of the DID the
    /// participant currently holds. Labels come from the identity collection, never the row.</summary>
    public async Task<ParticipantProfile> Read(string participantId, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var did = await directory.AtprotoDidOf(participantId, ct) ?? throw new UnauthorizedAccessException();
        var handle = await directory.LabelOf(participantId, ct);
        if (cache.TryGetValue<ParticipantProfile>("tangent-profile:" + did, out var saved) && saved is not null)
            return saved with { Handle = handle };
        var profile = new ParticipantProfile(did, handle, null, null, null, "unavailable");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var document = await sessions.ResolveDid(did, timeout.Token);
            var pds = document.PdsEndpoint?.TrimEnd('/') ?? throw new InvalidOperationException();
            var expected = "at://" + did + "/app.bsky.actor.profile/self";
            using var request = new HttpRequestMessage(HttpMethod.Get, pds + "/xrpc/com.atproto.repo.getRecord?repo="
                + Uri.EscapeDataString(did) + "&collection=app.bsky.actor.profile&rkey=self");
            using var response = await network.Send(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                var root = json.RootElement;
                if (root.GetProperty("uri").GetString() != expected) throw new InvalidDataException();
                var value = root.GetProperty("value");
                string? Text(string name, int limit) => value.TryGetProperty(name, out var field)
                    && field.ValueKind == JsonValueKind.String && field.GetString() is { } text ? text[..Math.Min(text.Length, limit)] : null;
                string? avatar = null;
                if (value.TryGetProperty("avatar", out var blob) && blob.TryGetProperty("mimeType", out var mime)
                    && mime.GetString() is "image/png" or "image/jpeg" or "image/webp" or "image/gif"
                    && blob.TryGetProperty("ref", out var reference) && reference.TryGetProperty("$link", out var link)
                    && link.GetString() is { Length: > 0 and < 200 } cid && cid.All(char.IsAsciiLetterOrDigit))
                    avatar = pds + "/xrpc/com.atproto.sync.getBlob?did=" + Uri.EscapeDataString(did) + "&cid=" + cid;
                profile = profile with { DisplayName = Text("displayName", 640), Description = Text("description", 2560), Avatar = avatar, Status = "loaded" };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { /* Missing profile or unavailable provider leaves the verified account usable. */ }
        cache.Set("tangent-profile:" + did, profile, TimeSpan.FromMinutes(profile.Status == "loaded" ? 5 : 1));
        return profile;
    }
}
