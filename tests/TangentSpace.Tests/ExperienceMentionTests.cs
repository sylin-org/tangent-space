using TangentSpace.Experience;
using Xunit;

namespace TangentSpace.Tests;

/// <summary>Deterministic mention-candidate tests: directed tokens, code fences, quoting,
/// lookalikes and DID addressing. These are pure parsing rules; resolution and access checks
/// apply after them in the digest.</summary>
public sealed class ExperienceMentionTests
{
    private const string Lumen = "did:plc:cccccccccccccccccccccccc";

    [Fact]
    public void Plain_handle_tokens_become_candidates()
    {
        var candidates = ExperienceMentions.Candidates("Hey @lumen.example.test, can you help?");
        Assert.Contains("lumen.example.test", candidates);
        Assert.Single(candidates);
    }

    [Fact]
    public void A_handle_inside_a_code_fence_never_counts()
    {
        var text = "Run this:\n```\necho @lumen.example.test\n```\nThanks!";
        Assert.Empty(ExperienceMentions.Candidates(text));
    }

    [Fact]
    public void Quoted_prose_without_a_token_yields_no_candidate_and_text_is_preserved()
    {
        // A quoted sentence that merely names someone is not a mention token.
        var candidates = ExperienceMentions.Candidates("Quoted from the proposal: \"You should review the whole plan.\"");
        Assert.Empty(candidates);
    }

    [Fact]
    public void Boundary_rules_exclude_embedded_lookalikes()
    {
        // An @ inside a word (email-like or glued) is not a mention token.
        Assert.Empty(ExperienceMentions.Candidates("Contact mail@lumen.example.test please"));
        Assert.Empty(ExperienceMentions.Candidates("Not@@lumen.example.test"));
    }

    [Fact]
    public void Trailing_punctuation_is_trimmed_and_duplicates_collapse()
    {
        var candidates = ExperienceMentions.Candidates("@lumen.example.test! Also @lumen.example.test.");
        Assert.Single(candidates);
        Assert.Equal("lumen.example.test", candidates[0]);
    }

    [Fact]
    public void Did_tokens_are_recognized_verbatim()
    {
        var candidates = ExperienceMentions.Candidates($"Planning with {Lumen} today.");
        Assert.Contains(Lumen, candidates);
    }

    [Fact]
    public void Addresses_matches_only_exact_handle_or_did_of_the_recipient()
    {
        Assert.True(ExperienceMentions.Addresses([Lumen], Lumen, "lumen.example.test"));
        Assert.True(ExperienceMentions.Addresses(["lumen.example.test"], Lumen, "lumen.example.test"));
        // Case-insensitive handle equality, like companion selection.
        Assert.True(ExperienceMentions.Addresses(["Lumen.Example.Test"], Lumen, "lumen.example.test"));
        // Another participant's handle does not address this one.
        Assert.False(ExperienceMentions.Addresses(["sol.example.test"], Lumen, "lumen.example.test"));
        // A lookalike display name is never a handle match.
        Assert.False(ExperienceMentions.Addresses(["lumen"], Lumen, "lumen.example.test"));
    }
}

/// <summary>Facet structure rules (ADR 0008): validation and group-token parsing.</summary>
public sealed class PostFacetTests
{
    [Fact]
    public void Valid_mention_facets_pass_and_order_by_range()
    {
        var text = "Hi @agent.example.test and #tag";
        var first = System.Text.Encoding.UTF8.GetByteCount("Hi ");
        var second = first + System.Text.Encoding.UTF8.GetByteCount("@agent.example.test");
        var facets = TangentSpace.Conversation.PostFacets.Check(text,
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
        Assert.Throws<ArgumentException>(() => TangentSpace.Conversation.PostFacets.Check("short",
            [new() { Kind = "mention", Start = 0, End = 99, Did = "did:plc:x" }]));
        Assert.Throws<ArgumentException>(() => TangentSpace.Conversation.PostFacets.Check("abcdef",
            [new() { Kind = "tag", Start = 0, End = 4, Value = "ab" }, new() { Kind = "tag", Start = 3, End = 6, Value = "de" }]));
        Assert.Throws<ArgumentException>(() => TangentSpace.Conversation.PostFacets.Check("x",
            [new() { Kind = "nonsense", Start = 0, End = 1 }]));
    }

    [Fact]
    public void A_mention_facet_carries_exactly_a_did()
    {
        Assert.Throws<ArgumentException>(() => TangentSpace.Conversation.PostFacets.Check("x",
            [new() { Kind = "mention", Start = 0, End = 1, Did = "did:plc:x", Value = "extra" }]));
        Assert.Throws<ArgumentException>(() => TangentSpace.Conversation.PostFacets.Check("x",
            [new() { Kind = "group", Start = 0, End = 1, Value = "not-a-group" }]));
    }

    [Fact]
    public void Group_tokens_parse_with_boundaries_and_fence_exclusion()
    {
        var groups = TangentSpace.Experience.ExperienceMentions.GroupCandidates(
            "Ping @admins and @moderators today", []);
        Assert.Equal(new[] { "admins", "moderators" }, groups);
        Assert.Empty(TangentSpace.Experience.ExperienceMentions.GroupCandidates(
            "```\n@admins\n```\nplain", []));
        Assert.Empty(TangentSpace.Experience.ExperienceMentions.GroupCandidates(
            "email@admins.example is not a group", []));
    }

    [Fact]
    public void An_exact_handle_collision_suppresses_the_group()
    {
        var groups = TangentSpace.Experience.ExperienceMentions.GroupCandidates(
            "Hey @members of the board", ["members"]);
        Assert.Empty(groups);
    }
}
