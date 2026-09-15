using TangentSpace.Application;
using Xunit;

namespace TangentSpace.Tests;

/// <summary>Rule-level tests for operation receipt keys, fingerprints and replay data.</summary>
public sealed class OperationReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 16, 0, 0, TimeSpan.Zero);

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
            OperationReceipt.CheckRequestId(candidate!);
            Assert.True(valid);
        }
        catch (ArgumentException)
        {
            Assert.False(valid);
        }
    }

    [Fact]
    public void Keys_bind_credential_participant_and_request_id()
    {
        var first = OperationReceipt.Key("credential-a", "participant-a", "reply-81");
        var second = OperationReceipt.Key("credential-b", "participant-a", "reply-81");
        var third = OperationReceipt.Key("credential-a", "participant-b", "reply-81");
        var repeat = OperationReceipt.Key("credential-a", "participant-a", "reply-81");
        Assert.Equal(first, repeat);
        Assert.NotEqual(first, second);
        Assert.NotEqual(first, third);
    }

    [Fact]
    public void Namespaced_operation_id_is_contract_safe_and_credential_scoped()
    {
        var first = OperationReceipt.BuildOperationId("credential-a", "participant-a", "reply-81");
        var second = OperationReceipt.BuildOperationId("credential-b", "participant-a", "reply-81");
        Assert.StartsWith("op-", first);
        Assert.Matches("^[A-Za-z0-9_-]{1,128}$", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Fingerprints_separate_operations_targets_and_payloads()
    {
        var text = OperationReceipt.FingerprintOf("CreatePost", "lounge", """{"replyTo":null,"text":"hi"}""");
        var same = OperationReceipt.FingerprintOf("CreatePost", "lounge", """{"replyTo":null,"text":"hi"}""");
        var changed = OperationReceipt.FingerprintOf("CreatePost", "lounge", """{"replyTo":null,"text":"hi!"}""");
        var moved = OperationReceipt.FingerprintOf("CreatePost", "workshop", """{"replyTo":null,"text":"hi"}""");
        var renamed = OperationReceipt.FingerprintOf("ReadPosition", "lounge", """{"replyTo":null,"text":"hi"}""");
        Assert.Equal(text, same);
        Assert.NotEqual(text, changed);
        Assert.NotEqual(text, moved);
        Assert.NotEqual(text, renamed);
    }

    [Fact]
    public void Canonical_payload_uses_stable_key_order_and_explicit_nulls()
    {
        var canonical = OperationReceipts.Canonical(new Dictionary<string, string?>
        {
            ["topicRef"] = "x",
            ["replyTo"] = null,
            ["text"] = "hi"
        });
        Assert.Equal("""{"replyTo":null,"text":"hi","topicRef":"x"}""", canonical);
    }

    [Fact]
    public void Completion_replays_the_stored_result_without_repeating_side_effects()
    {
        var receipt = new OperationReceipt { Id = "k", RequestId = "reply-81", State = "pending" };
        receipt.Complete("completed", "https://tangent.example::home::lounge::p1", """{"x":1}""", Now);
        Assert.Equal("completed", receipt.State);
        Assert.Equal("""{"x":1}""", receipt.ResultData);
        receipt.Complete("pending", null, null, Now);
        Assert.Equal("completed", receipt.State);
    }
}
