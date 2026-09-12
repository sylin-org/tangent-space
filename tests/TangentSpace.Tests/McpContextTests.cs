using TangentSpace.Mcp;
using Xunit;

namespace TangentSpace.Tests;

/// <summary>Rule-level tests for the durable MCP companion selection and participation context:
/// binding, rolling expiry, moniker matching, and the immutable server binding.</summary>
public sealed class McpContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 16, 0, 0, TimeSpan.Zero);
    private const string Did = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Context_identifier_is_prefixed_and_long_enough_to_be_opaque()
    {
        var identifier = McpContext.NewIdentifier();
        Assert.StartsWith("ctx_", identifier);
        Assert.InRange(identifier.Length, 8, 96);
        Assert.DoesNotContain("+", identifier);
        Assert.DoesNotContain("/", identifier);
    }

    [Fact]
    public void Selection_identifier_is_prefixed_and_long_enough_to_be_opaque()
    {
        var identifier = McpSelection.NewIdentifier();
        Assert.StartsWith("cmp_", identifier);
        Assert.InRange(identifier.Length, 8, 96);
        Assert.DoesNotContain("+", identifier);
        Assert.DoesNotContain("/", identifier);
    }

    [Fact]
    public void Issue_binds_credential_and_did_and_starts_the_rolling_window()
    {
        var (selection, created) = McpSelection.Issue("credential-hash", Did, Now);
        Assert.True(created);
        Assert.Equal("credential-hash", selection.CredentialId);
        Assert.Equal(Did, selection.ParticipantId);
        Assert.True(selection.IsUsable(Now.AddHours(23)));
        Assert.False(selection.IsUsable(Now.AddHours(25)));
    }

    [Fact]
    public void Context_issue_requires_a_companion_selection_and_a_canonical_origin()
    {
        var (selection, _) = McpSelection.Issue("credential-hash", Did, Now);
        var (context, created) = McpContext.Issue("credential-hash", Did, selection.Id, "https://tangent.example", Now);
        Assert.True(created);
        Assert.Equal(selection.Id, context.CompanionId);
        Assert.Equal("https://tangent.example", context.Origin);
        Assert.True(context.IsBound);
        Assert.True(context.IsUsable(Now.AddHours(23)));
        Assert.False(context.IsUsable(Now.AddHours(25)));
        Assert.Throws<ArgumentException>(() => McpContext.Issue("credential-hash", Did, "ctx_not_a_selection", "https://tangent.example", Now));
        Assert.Throws<ArgumentException>(() => McpContext.Issue("credential-hash", Did, selection.Id, "", Now));
    }

    [Fact]
    public void Touch_renews_without_changing_the_binding()
    {
        var (selection, _) = McpSelection.Issue("credential-hash", Did, Now);
        var (context, _) = McpContext.Issue("credential-hash", Did, selection.Id, "https://tangent.example", Now);
        context.Touch(Now.AddHours(12));
        selection.Touch(Now.AddHours(12));
        Assert.Equal("credential-hash", context.CredentialId);
        Assert.Equal(Did, context.ParticipantId);
        Assert.Equal(selection.Id, context.CompanionId);
        Assert.Equal("https://tangent.example", context.Origin);
        Assert.True(context.IsUsable(Now.AddHours(35)));
        Assert.False(context.IsUsable(Now.AddHours(37)));
        Assert.True(selection.IsUsable(Now.AddHours(35)));
    }

    [Fact]
    public void Touch_never_moves_the_window_backwards_in_time()
    {
        var (selection, _) = McpSelection.Issue("credential-hash", Did, Now);
        var (context, _) = McpContext.Issue("credential-hash", Did, selection.Id, "https://tangent.example", Now);
        context.Touch(Now.AddHours(-1));
        selection.Touch(Now.AddHours(-1));
        Assert.False(context.IsUsable(Now.AddHours(25)));
        Assert.True(context.IsUsable(Now.AddHours(23)));
        Assert.False(selection.IsUsable(Now.AddHours(25)));
    }

    [Fact]
    public void Pre_split_contexts_without_a_binding_are_not_bound()
    {
        // Rows persisted before the companion split carry no companion or origin; they must read
        // as unbound so resolve treats them as expired instead of resurrecting them.
        var legacy = new McpContext { Id = "ctx_legacy", CredentialId = "credential-hash", ParticipantId = Did };
        Assert.False(legacy.IsBound);
    }

    private static readonly string ParticipantId = Participants.Participant.NewIdentifier();

    [Fact]
    public void Moniker_matches_an_identity_value_or_label_with_or_without_one_leading_at()
    {
        var participant = new Participants.Participant { Id = ParticipantId, JoinedAt = Now };
        var identities = new Participants.ParticipantIdentity[]
        {
            Participants.ParticipantIdentity.Atproto(ParticipantId, Did, "lumen.example", Now),
            Participants.ParticipantIdentity.Internal(ParticipantId, Now)
        };
        Assert.True(CompanionIdentity.Matches("lumen.example", participant, identities));
        Assert.True(CompanionIdentity.Matches("@lumen.example", participant, identities));
        Assert.True(CompanionIdentity.Matches("Lumen.Example", participant, identities));
        Assert.True(CompanionIdentity.Matches(Did, participant, identities));
        Assert.True(CompanionIdentity.Matches(identities[1].Value, participant, identities));
        Assert.False(CompanionIdentity.Matches("someone.example", participant, identities));
        Assert.False(CompanionIdentity.Matches("@@lumen.example", participant, identities));
        Assert.False(CompanionIdentity.Matches("", participant, identities));
    }

    [Fact]
    public void Display_name_derives_from_the_best_label_without_leaking_secrets()
    {
        Assert.Equal("lumen.example", CompanionIdentity.ActingAs("lumen.example", Did));
        Assert.Equal("lumen", CompanionIdentity.DisplayName("lumen.example", Did));
        Assert.Equal(Did, CompanionIdentity.DisplayName(null, Did));
    }
}

/// <summary>Rule-level tests for the durable request registry keys, fingerprints and replay data.</summary>
public sealed class McpRequestTests
{
    [Theory]
    [InlineData("reply-81")]
    [InlineData("A")]
    [InlineData("a" + "0123456789_")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad id")]
    [InlineData("id/with-slash")]
    [InlineData("id.with.dots")]
    public void Request_ids_follow_the_contract_pattern(string? candidate)
    {
        var valid = candidate is "reply-81" or "A" or "a0123456789_";
        try
        {
            McpRequestRecord.CheckRequestId(candidate!);
            Assert.True(valid);
        }
        catch (ArgumentException)
        {
            Assert.False(valid);
        }
    }

    [Fact]
    public void Keys_bind_credential_did_and_request_id_without_reuse_across_runtimes()
    {
        var first = McpRequestRecord.Key("credential-a", "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", "reply-81");
        var second = McpRequestRecord.Key("credential-b", "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", "reply-81");
        var third = McpRequestRecord.Key("credential-a", "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb", "reply-81");
        var repeat = McpRequestRecord.Key("credential-a", "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", "reply-81");
        Assert.Equal(first, repeat);
        Assert.NotEqual(first, second);
        Assert.NotEqual(first, third);
    }

    [Fact]
    public void Namespaced_operation_id_is_contract_safe_and_runtime_scoped()
    {
        var first = McpRequestRecord.BuildOperationId("credential-a", "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", "reply-81");
        var second = McpRequestRecord.BuildOperationId("credential-b", "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", "reply-81");
        Assert.StartsWith("mcp-", first);
        Assert.Matches("^[A-Za-z0-9_-]{1,128}$", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Fingerprints_separate_operations_targets_and_payloads()
    {
        var text = McpRequestRecord.FingerprintOf("PostMessage", "lounge", """{"replyTo":null,"text":"hi"}""");
        var same = McpRequestRecord.FingerprintOf("PostMessage", "lounge", """{"replyTo":null,"text":"hi"}""");
        var changed = McpRequestRecord.FingerprintOf("PostMessage", "lounge", """{"replyTo":null,"text":"hi!"}""");
        var moved = McpRequestRecord.FingerprintOf("PostMessage", "workshop", """{"replyTo":null,"text":"hi"}""");
        var renamed = McpRequestRecord.FingerprintOf("MarkRead", "lounge", """{"replyTo":null,"text":"hi"}""");
        Assert.Equal(text, same);
        Assert.NotEqual(text, changed);
        Assert.NotEqual(text, moved);
        Assert.NotEqual(text, renamed);
    }

    [Fact]
    public void Canonical_payload_uses_stable_key_order_and_explicit_nulls()
    {
        var canonical = McpRequests.Canonical(new Dictionary<string, string?>
        {
            ["replyTo"] = null,
            ["text"] = "hi",
            ["channelRef"] = "x"
        });
        Assert.Equal("""{"channelRef":"x","replyTo":null,"text":"hi"}""", canonical);
    }

    [Fact]
    public void Completion_replays_the_stored_result_without_repeating_side_effects()
    {
        var record = new McpRequestRecord { Id = "k", RequestId = "reply-81", State = "pending" };
        record.Complete("completed", "https://tangent.example::home::lounge::m1", """{"x":1}""", Now2());
        Assert.Equal("completed", record.State);
        Assert.Equal("""{"x":1}""", record.ResultData);
        record.Complete("pending", null, null, Now2());
        Assert.Equal("completed", record.State);
    }

    private static DateTimeOffset Now2() => new(2026, 9, 10, 16, 0, 0, TimeSpan.Zero);
}
