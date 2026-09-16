using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Tangent.Application;
using Tangent.Spaces;
using Xunit;

namespace Tangent.Tests;

public sealed class ReferencesTests
{
    private static References Create(string origin = "https://tangent.example")
        => new(Options.Create(new SpaceOptions { PublicOrigin = origin }), new EphemeralDataProtectionProvider());

    [Theory]
    [InlineData("https://evil.example::home::lounge")]
    [InlineData("https://tangent.example.evil::home::lounge")]
    [InlineData("https://tangent.example::home::../lounge")]
    [InlineData("https://tangent.example::home::lounge::post")]
    public void Topic_references_reject_foreign_origins_and_wrong_shapes(string value)
        => Assert.Null(Create().ParseTopic(value));

    [Fact]
    public void Moderation_case_references_are_qualified_and_type_safe()
    {
        var references = Create();
        var id = new string('a', 64);
        var value = references.Case("home", "lounge", id);
        Assert.Equal(("home", "lounge", id), references.ParseCase(value));
        Assert.Null(references.ParseCase(references.Post("home", "lounge", id)));
        Assert.Null(references.ParsePost(value));
        Assert.Null(references.ParseCase($"https://evil.example::home::lounge::case_{id}"));
        Assert.Null(references.ParseCase(references.Case("home", "lounge", new string('A', 64))));
    }

    [Fact]
    public void Post_references_accept_the_hyphenated_ids_the_server_emits()
    {
        var references = Create();
        Assert.Equal(("home", "lounge", "p-source-1"), references.ParsePost(references.Post("home", "lounge", "p-source-1")));
    }

    [Fact]
    public void Invitation_references_round_trip_and_are_not_topics()
    {
        var references = Create();
        const string invitation = "0123456789abcdef0123456789abcdef";
        var value = references.Invite("home", invitation);
        Assert.Equal(("home", invitation), references.ParseInvite(value));
        Assert.Null(references.ParseTopic(value));
        Assert.Null(references.ParseInvite(references.Topic("home", "lounge")));
    }

    [Fact]
    public void Directory_cursors_are_bound_to_scope_and_participant()
    {
        var references = Create();
        var value = references.EncodeListCursor("home", "participant-a", 2, null);
        Assert.Equal(2, references.DecodeListCursor(value, "home", "participant-a")!.Page);
        Assert.Throws<ArgumentException>(() => references.DecodeListCursor(value, "other", "participant-a"));
        Assert.Throws<ArgumentException>(() => references.DecodeListCursor(value, "home", "participant-b"));
        Assert.Throws<ArgumentException>(() => references.DecodeListCursor("not-a-cursor", "home", "participant-a"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://tangent.example")]
    [InlineData("http://tangent.example")]
    [InlineData("https://tangent.example/path")]
    [InlineData("https://user@tangent.example")]
    public void A_missing_or_non_canonical_public_origin_is_rejected(string origin)
        => Assert.Throws<InvalidOperationException>(() => Create(origin));
}
