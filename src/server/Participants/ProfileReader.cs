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

/// <summary>A public profile as the profile service answered it: the DID it is for, what the profile holds, its banner among
/// it, and whether the account labels itself a bot on its own profile. The handle is not in it: the place shows the handle the
/// connector verified at sign-in.</summary>
public sealed record ProfileAnswer(string Did, string? DisplayName, string? Description, string? Avatar, bool AgentLabel = false, string? Banner = null);

/// <summary>Why a profile could not be read: the service did not answer in time, answered past the bound, refused, or
/// answered something that is not this DID's profile. Nothing was cached.</summary>
public sealed class ProfileReadFailed(string reason, Exception? inner = null) : Exception(reason, inner);

/// <summary>The one public read of a participant's profile: <c>app.bsky.actor.getProfile</c> for a DID, one request,
/// no redirect, bounded in time and in bytes. It runs in the profile job, never in a request and never in a
/// command; the same read for a handle, which an invitation makes in its request, outside any command, to learn whose
/// account the handle names (<see cref="Find"/>); and the one read of a picture such a profile named, which a write that
/// brings it in makes.</summary>
public sealed class ProfileReader(HttpClient http, IOptions<ProfileReaderOptions> options)
{
    /// <summary>The most characters of a display name kept.</summary>
    public const int DisplayNameLimit = 640;
    private const int DescriptionLimit = 2560;
    private const int PictureLimit = 2048;

    /// <summary>The label value an account sets on its own profile to say it is automated: one of the known values
    /// of <c>com.atproto.label.defs#labelValue</c>.</summary>
    public const string AgentLabelValue = "bot";

    /// <summary>The public profile of the account <paramref name="did"/>.</summary>
    public async Task<ProfileAnswer> Read(string did, CancellationToken ct)
        => Parse(did, await Fetch(did, ct), root => Text(root, "did", int.MaxValue) == did ? did : null);

    /// <summary>The public profile of the account the handle <paramref name="handle"/> names, as atproto writes handles,
    /// with the DID the answer carries: taken only where the answer says that handle, so a handle the service could not
    /// verify, which it answers as <c>handle.invalid</c>, names no account.</summary>
    public async Task<ProfileAnswer> Find(string handle, CancellationToken ct)
        => Parse(handle, await Fetch(handle, ct), root =>
            string.Equals(Text(root, "handle", SignIn.HandleLimit + 1), handle, StringComparison.OrdinalIgnoreCase)
            && Text(root, "did", Posts.Mentions.IdentityLimit + 1) is { } did && Participant.IsDid(did) ? did : null);

    /// <summary>The bytes the profile service answers for <paramref name="actor"/>, a DID or a handle.</summary>
    private async Task<byte[]> Fetch(string actor, CancellationToken ct)
    {
        var settings = options.Value;
        var address = new Uri(AsFolder(settings.BaseAddress), "xrpc/app.bsky.actor.getProfile?actor=" + Uri.EscapeDataString(actor));
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(settings.Timeout);
        try
        {
            using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, bounded.Token);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new ProfileReadFailed($"The profile service answered {(int)response.StatusCode} for {actor}.");
            if (response.Content.Headers.ContentLength > settings.MaximumResponseBytes)
                throw new ProfileReadFailed($"The profile of {actor} is larger than {settings.MaximumResponseBytes} bytes.");
            return await ReadBounded(response.Content, settings.MaximumResponseBytes, actor, bounded.Token);
        }
        catch (OperationCanceledException timeout) when (!ct.IsCancellationRequested)
        {
            throw new ProfileReadFailed($"The profile service did not answer for {actor} within {settings.Timeout}.", timeout);
        }
        catch (HttpRequestException unreachable)
        {
            throw new ProfileReadFailed($"The profile service could not be reached for {actor}.", unreachable);
        }
    }

    /// <summary>The base address as a folder, so the read's relative path lands under its path.</summary>
    /// <summary>The bytes of a picture a profile named at <paramref name="address"/>, at most <paramref name="limit"/>: one
    /// https request, no redirect, bounded in time as a profile's read is.</summary>
    public async Task<byte[]> Picture(string address, int limit, CancellationToken ct)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var picture) || picture.Scheme != Uri.UriSchemeHttps)
            throw new ProfileReadFailed($"The picture at {address} is not an https address.");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(options.Value.Timeout);
        try
        {
            using var response = await http.GetAsync(picture, HttpCompletionOption.ResponseHeadersRead, bounded.Token);
            if (response.StatusCode != HttpStatusCode.OK) throw new ProfileReadFailed($"The picture at {address} answered {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > limit) throw new ProfileReadFailed($"The picture at {address} is larger than {limit} bytes.");
            return await ReadBounded(response.Content, limit, address, bounded.Token);
        }
        catch (OperationCanceledException timeout) when (!ct.IsCancellationRequested)
        {
            throw new ProfileReadFailed($"The picture at {address} did not arrive within {options.Value.Timeout}.", timeout);
        }
        catch (HttpRequestException unreachable)
        {
            throw new ProfileReadFailed($"The picture at {address} could not be reached.", unreachable);
        }
    }

    private static Uri AsFolder(Uri baseAddress)
        => baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");

    private static async Task<byte[]> ReadBounded(HttpContent content, int limit, string subject, CancellationToken ct)
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
        throw new ProfileReadFailed($"What {subject} answered is larger than {limit} bytes.");
    }

    /// <summary>The profile <paramref name="body"/> holds, for the account <paramref name="owner"/> finds it is, where it is
    /// the one asked for as <paramref name="actor"/>.</summary>
    private static ProfileAnswer Parse(string actor, byte[] body, Func<JsonElement, string?> owner)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (owner(root) is not { } did)
                throw new ProfileReadFailed($"The profile service answered another account's profile for {actor}.");
            return new ProfileAnswer(did, Text(root, "displayName", DisplayNameLimit),
                Text(root, "description", DescriptionLimit), PictureAt(root, "avatar"), AgentLabelled(root, did), PictureAt(root, "banner"));
        }
        catch (JsonException malformed)
        {
            throw new ProfileReadFailed($"The profile service answered something other than JSON for {actor}.", malformed);
        }
    }

    /// <summary>The https address a profile names a picture at, where it names one within the bound.</summary>
    private static string? PictureAt(JsonElement root, string name)
        => Text(root, name, PictureLimit + 1) is { Length: <= PictureLimit } address
           && Uri.TryCreate(address, UriKind.Absolute, out var parsed) && parsed.Scheme == "https" ? address : null;

    /// <summary>Whether the answer's <c>labels</c> hold the bot label the account set itself: the profile view carries
    /// the profile record's self-labels as labels whose source is the account, beside any a labeler applied, which do
    /// not count. A negated label says nothing.</summary>
    private static bool AgentLabelled(JsonElement root, string did)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty("labels", out var labels)
           && labels.ValueKind == JsonValueKind.Array
           && labels.EnumerateArray().Any(label =>
               Text(label, "val", AgentLabelValue.Length + 1) == AgentLabelValue
               && Text(label, "src", did.Length + 1) == did
               && !(label.TryGetProperty("neg", out var negated) && negated.ValueKind == JsonValueKind.True));

    private static string? Text(JsonElement root, string name, int limit)
        => root.ValueKind == JsonValueKind.Object
           && root.TryGetProperty(name, out var field)
           && field.ValueKind == JsonValueKind.String
           && field.GetString() is { Length: > 0 } text
            ? text[..Math.Min(text.Length, limit)]
            : null;
}
