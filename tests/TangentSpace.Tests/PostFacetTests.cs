using TangentSpace.Conversation;
using TangentSpace.Participants;
using Xunit;

namespace TangentSpace.Tests;

/// <summary>Facet rules (ADR 0008): structural validation, and the detection that gives every
/// saved post its mention and group facets.</summary>
public sealed class PostFacetTests
{
    private const string Lumen = "did:plc:cccccccccccccccccccccccc";

    [Fact]
    public void Valid_mention_facets_pass_and_order_by_range()
    {
        var text = "Hi @agent.example.test and #tag";
        var first = System.Text.Encoding.UTF8.GetByteCount("Hi ");
        var second = first + System.Text.Encoding.UTF8.GetByteCount("@agent.example.test");
        var facets = PostFacets.Check(text,
        [
            new() { Kind = "tag", Start = second, End = second + 4, Value = "tag" },
            new() { Kind = "mention", Start = first, End = second, Did = "did:plc:x" },
        ]);
        Assert.NotNull(facets);
        Assert.Equal(first, facets![0].Start);
        Assert.Equal("mention", facets[0].Kind);
    }

    [Fact]
    public void Out_of_range_or_overlapping_facets_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => PostFacets.Check("short",
            [new() { Kind = "mention", Start = 0, End = 99, Did = "did:plc:x" }]));
        Assert.Throws<ArgumentException>(() => PostFacets.Check("abcdef",
            [new() { Kind = "tag", Start = 0, End = 4, Value = "ab" }, new() { Kind = "tag", Start = 3, End = 6, Value = "de" }]));
        Assert.Throws<ArgumentException>(() => PostFacets.Check("x",
            [new() { Kind = "nonsense", Start = 0, End = 1 }]));
    }

    [Fact]
    public void A_mention_facet_carries_exactly_a_did()
    {
        Assert.Throws<ArgumentException>(() => PostFacets.Check("x",
            [new() { Kind = "mention", Start = 0, End = 1, Did = "did:plc:x", Value = "extra" }]));
        Assert.Throws<ArgumentException>(() => PostFacets.Check("x",
            [new() { Kind = "group", Start = 0, End = 1, Value = "not-a-group" }]));
    }

    [Fact]
    public void Handle_and_did_tokens_become_one_mention_outside_code_fences()
    {
        var lumen = Identity("lumen", Lumen, "lumen.example.test");

        var mention = Assert.Single(MessageFacets.Detect($"Hey @lumen.example.test! Also @Lumen.Example.Test and {Lumen}.", [lumen]));

        Assert.Equal(PostFacet.Mention, mention.Kind);
        Assert.Equal(Lumen, mention.Did);
        Assert.Empty(MessageFacets.Detect("Run this:\n```\necho @lumen.example.test\n```\nThanks!", [lumen]));
        Assert.Empty(MessageFacets.Detect("Contact mail@lumen.example.test please", [lumen]));
    }

    [Fact]
    public void An_ambiguous_handle_stays_plain_text()
        => Assert.Empty(MessageFacets.Detect("Ping @lumen.example.test",
            [Identity("first", Lumen, "lumen.example.test"), Identity("second", "did:plc:dddddddddddddddddddddddd", "lumen.example.test")]));

    [Fact]
    public void Group_tokens_become_facets_at_word_boundaries_outside_code_fences()
    {
        Assert.Equal(new[] { "admins", "moderators" },
            MessageFacets.Detect("Ping @admins and @moderators today", []).Select(facet => facet.Value!));
        Assert.Empty(MessageFacets.Detect("```\n@admins\n```\nplain", []));
        Assert.Empty(MessageFacets.Detect("email@admins.example is not a group", []));
    }

    [Fact]
    public void A_stored_handle_that_spells_a_group_wins_over_the_group()
    {
        var mention = Assert.Single(MessageFacets.Detect("Hey @members of the board", [Identity("board", Lumen, "members")]));

        Assert.Equal(PostFacet.Mention, mention.Kind);
        Assert.Equal(Lumen, mention.Did);
    }

    private static ParticipantIdentity Identity(string participantId, string did, string label)
        => new() { Kind = ParticipantIdentity.AtprotoKind, Value = did, Label = label, ParticipantId = participantId };
}
