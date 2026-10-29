using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Tangent.Participants;

/// <summary>What an operator configures under <c>Tangent:Profile</c>: where public profiles are read, how long one
/// read may take and how large its answer may be.</summary>
public sealed class ProfileReaderOptions
{
    public const string Configuration = "Tangent:Profile";

    /// <summary>The public atproto service that answers <c>app.bsky.actor.getProfile</c> without a session.</summary>
    public Uri BaseAddress { get; set; } = new("https://public.api.bsky.app/");

    /// <summary>The whole read, from the request to the last byte of the answer.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The largest answer read; a longer one is refused unread past this bound.</summary>
    public int MaximumResponseBytes { get; set; } = 64 * 1024;
}

/// <summary>A public profile as the profile service answered it: the DID it is for, the handle, and what the profile
/// holds.</summary>
public sealed record ProfileAnswer(string Did, string Handle, string? DisplayName, string? Description, string? Avatar);

/// <summary>Why a profile could not be read: the service did not answer in time, answered past the bound, refused, or
/// answered something that is not this DID's profile. Nothing was cached.</summary>
public sealed class ProfileReadFailed(string reason, Exception? inner = null) : Exception(reason, inner);

/// <summary>The one public read of a participant's profile: <c>app.bsky.actor.getProfile</c> for a DID, one request,
/// no redirect, bounded in time and in bytes. It runs in the profile job, never in a request and never in the
/// writer.</summary>
public sealed class ProfileReader(HttpClient http, IOptions<ProfileReaderOptions> options)
{
    private const int DisplayNameLimit = 640;
    private const int DescriptionLimit = 2560;
    private const int HandleLimit = 253;
    private const int AvatarLimit = 2048;

    public async Task<ProfileAnswer> Read(string did, CancellationToken ct)
    {
        var settings = options.Value;
        var address = new Uri(AsFolder(settings.BaseAddress), "xrpc/app.bsky.actor.getProfile?actor=" + Uri.EscapeDataString(did));
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(settings.Timeout);
        byte[] body;
        try
        {
            using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, bounded.Token);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new ProfileReadFailed($"The profile service answered {(int)response.StatusCode} for {did}.");
            if (response.Content.Headers.ContentLength > settings.MaximumResponseBytes)
                throw new ProfileReadFailed($"The profile of {did} is larger than {settings.MaximumResponseBytes} bytes.");
            body = await ReadBounded(response.Content, settings.MaximumResponseBytes, did, bounded.Token);
        }
        catch (OperationCanceledException timeout) when (!ct.IsCancellationRequested)
        {
            throw new ProfileReadFailed($"The profile service did not answer for {did} within {settings.Timeout}.", timeout);
        }
        catch (HttpRequestException unreachable)
        {
            throw new ProfileReadFailed($"The profile service could not be reached for {did}.", unreachable);
        }

        return Parse(did, body);
    }

    /// <summary>The base address as a folder, so the read's relative path lands under its path.</summary>
    private static Uri AsFolder(Uri baseAddress)
        => baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");

    private static async Task<byte[]> ReadBounded(HttpContent content, int limit, string did, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[limit + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (count == 0) return buffer[..read];
            read += count;
        }
        throw new ProfileReadFailed($"The profile of {did} is larger than {limit} bytes.");
    }

    private static ProfileAnswer Parse(string did, byte[] body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (Text(root, "did", int.MaxValue) != did)
                throw new ProfileReadFailed($"The profile service answered another DID's profile for {did}.");
            var handle = Text(root, "handle", HandleLimit + 1);
            if (string.IsNullOrWhiteSpace(handle) || handle.Length > HandleLimit)
                throw new ProfileReadFailed($"The profile of {did} carries no handle.");
            var avatar = Text(root, "avatar", AvatarLimit + 1);
            var avatarAddress = avatar is { Length: <= AvatarLimit }
                && Uri.TryCreate(avatar, UriKind.Absolute, out var parsed)
                && parsed.Scheme is "https" or "http" ? avatar : null;
            return new ProfileAnswer(did, handle, Text(root, "displayName", DisplayNameLimit),
                Text(root, "description", DescriptionLimit), avatarAddress);
        }
        catch (JsonException malformed)
        {
            throw new ProfileReadFailed($"The profile service answered something other than JSON for {did}.", malformed);
        }
    }

    private static string? Text(JsonElement root, string name, int limit)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty(name, out var field)
           && field.ValueKind == JsonValueKind.String
           && field.GetString() is { Length: > 0 } text
            ? text[..Math.Min(text.Length, limit)]
            : null;
}
