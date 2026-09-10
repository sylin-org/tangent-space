using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TangentSpace.AtProtocol;
using Xunit;

namespace TangentSpace.Tests;

public sealed class SourceNotificationTests
{
    private static readonly SpacesOptions Options = new() { AuthorityDid = "did:plc:abcdefghijklmnopqrstuvwx" };
    private static SourceNotificationRequest Request(string? space = null, string? repo = null, string? revision = null, object? hash = null)
        => new(space ?? Options.Space("workshop"), repo ?? "did:plc:zyxwvutsrqponmlkjihgfedcb", revision ?? "3m2za2bcdefgh",
            JToken.FromObject(hash ?? new Dictionary<string, string> { ["$bytes"] = Convert.ToBase64String(new byte[32]).TrimEnd('=') }));

    [Fact]
    public void Native_notification_binds_through_the_Koan_Mvc_serializer()
    {
        var json = """
            {"space":"at://did:plc:abcdefghijklmnopqrstuvwx/space/local.tangent.room/workshop",
             "repo":"did:plc:zyxwvutsrqponmlkjihgfedcb","rev":"3m2za2bcdefgh",
             "hash":{"$bytes":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}}
            """;
        var request = JsonConvert.DeserializeObject<SourceNotificationRequest>(json);
        Assert.NotNull(request);
        Assert.True(request.TryValidate(Options, out var room, out var hash));
        Assert.Equal("workshop", room);
        Assert.Equal(Convert.ToBase64String(new byte[32]), hash);
        Assert.False((request with { Hash = null }).TryValidate(Options, out _, out _));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void Native_lexicon_hash_accepts_unpadded_and_padded_base64(bool padded)
    {
        var hash = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        var request = Request(hash: new Dictionary<string, string> { ["$bytes"] = padded ? hash : hash.TrimEnd('=') });
        Assert.True(request.TryValidate(Options, out var room, out var canonical));
        Assert.Equal("workshop", room);
        Assert.Equal(hash, canonical);
    }

    [Theory, InlineData("https://example.org/workshop"), InlineData("at://did:plc:zyxwvutsrqponmlkjihgfedcb/space/local.tangent.room/workshop"),
        InlineData("at://did:plc:abcdefghijklmnopqrstuvwx/space/local.tangent.room/../workshop")]
    public void Rejects_unrelated_authorities_and_invalid_room_keys(string space)
        => Assert.False(Request(space: space).TryValidate(Options, out _, out _));

    [Theory, InlineData("not-a-did", "3m2za2bcdefgh"), InlineData("did:plc:zyxwvutsrqponmlkjihgfedcb", "not-a-revision")]
    public void Rejects_invalid_writer_or_revision(string repo, string revision)
        => Assert.False(Request(repo: repo, revision: revision).TryValidate(Options, out _, out _));

    [Theory, InlineData("AA"), InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA "),
        InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB"), InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA===")]
    public void Rejects_wrong_size_noncanonical_or_malformed_hash(string hash)
        => Assert.False(Request(hash: new Dictionary<string, string> { ["$bytes"] = hash }).TryValidate(Options, out _, out _));

    [Fact]
    public void Does_not_accept_arbitrary_json_in_place_of_a_lexicon_hash()
    {
        Assert.False(Request(hash: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA").TryValidate(Options, out _, out _));
        Assert.False(Request(hash: new Dictionary<string, object> { ["$bytes"] = new string('A', 43), ["extra"] = true }).TryValidate(Options, out _, out _));
    }
}
