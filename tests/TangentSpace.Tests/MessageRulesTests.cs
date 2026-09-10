using System.Formats.Cbor;
using System.Text.Json;
using CarpaNet;
using TangentSpace.AtProtocol;
using TangentSpace.Conversation;
using Xunit;

namespace TangentSpace.Tests;

public sealed class MessageRulesTests
{
    private const string ReplyUri = "at://did:plc:yblwluenzocgiqpmu4ip7six/space/local.tangent.room/tangent-workshop/did:plc:mgxxqowf6nnd3btckqjq573l/local.tangent.message/op-39725df245a4ccc34dcefe09bfdc4e6d6d4bdc38ae7f7dd070028859cf79f256";
    private const string ReplyCid = "bafyreidvmsh4woms3l3flgzzn6763qlrl7hrirx2hbzbnxs2oxyhtfdegy";
    [Fact]
    public void Utf8LimitCountsBytesRatherThanCharacters()
    {
        MessageContent.CheckText(new string('é', 2048));
        Assert.Throws<ArgumentException>(() => MessageContent.CheckText(new string('é', 2049)));
        Assert.Throws<ArgumentException>(() => MessageContent.CheckText(" \n"));
        Assert.Throws<ArgumentException>(() => MessageContent.CheckText("text\0"));
    }

    [Fact]
    public void AttributedPostCannotSupplyAnAuthor()
    {
        const string forged = """{"operationId":"one","text":"hi","authorDid":"did:plc:someoneelse"}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PostMessage>(forged));
        Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => Newtonsoft.Json.JsonConvert.DeserializeObject<PostMessage>(forged));
    }

    [Fact]
    public void SourceValidationRejectsExtraAuthorAndWrongCollection()
    {
        var created = DateTimeOffset.Parse("2026-09-09T12:00:00Z");
        Assert.Equal("hello", MessageContent.FromCbor(Record("hello", SpacesOptions.Collection, created)).Text);
        Assert.Throws<InvalidDataException>(() => MessageContent.FromCbor(Record("hello", "app.bsky.feed.post", created)));
        Assert.Throws<InvalidDataException>(() => MessageContent.FromCbor(Record("hello", SpacesOptions.Collection, created, "did:plc:forged")));
    }

    [Fact]
    public void RecordClockDoesNotDetermineDecisionClockOrOrdering()
    {
        var old = MessageContent.FromCbor(Record("late", SpacesOptions.Collection, DateTimeOffset.UnixEpoch));
        var decision = new SourceDecision { Id = "source", Accepted = true, DecidedAt = DateTimeOffset.UtcNow,
            Sequence = 42, Content = old, AuthorDid = "did:plc:verified-source" };
        var projected = Message.Project(decision);
        Assert.Equal(DateTimeOffset.UnixEpoch, projected.Content.CreatedAt);
        Assert.Equal(decision.DecidedAt, projected.AcceptedAt);
        Assert.Equal(42, projected.Sequence);
        Assert.Equal(decision.AuthorDid, projected.AuthorDid);
    }

    [Theory]
    [InlineData("2026-09-09T21:17:06Z", "2026-09-09T21:17:06.0000000+00:00")]
    [InlineData("2026-09-09T21:17:06.745Z", "2026-09-09T21:17:06.7450000+00:00")]
    [InlineData("2026-09-09T21:17:06.7459069+00:00", "2026-09-09T21:17:06.7459069+00:00")]
    [InlineData("2026-09-09T17:17:06.7459069-04:00", "2026-09-09T21:17:06.7459069+00:00")]
    [InlineData("2026-09-09T21:17:06.123456789012Z", "2026-09-09T21:17:06.1234567+00:00")]
    [InlineData("2026-09-09T21:17:06+15:30", "2026-09-09T05:47:06.0000000+00:00")]
    public void RealSourceTimestampFormsAndValidRepliesAreRetained(string timestamp, string expected)
    {
        var content = MessageContent.FromCbor(SourceRecord(timestamp, new(ReplyUri, ReplyCid)));
        Assert.Equal(DateTimeOffset.Parse(expected), content.CreatedAt);
        Assert.Equal(new SourceReference(ReplyUri, ReplyCid), content.ReplyTo);
        Assert.Equal(content, MessageContent.FromCbor(SourceRecord(content.ToRecord().GetProperty("createdAt").GetString()!, content.ReplyTo)));
    }

    [Theory]
    [InlineData("September 9, 2026")]
    [InlineData("2026-09-09")]
    [InlineData("2026-09-09 12:00:00Z")]
    [InlineData("2026-09-09T12:00:00")]
    [InlineData("2026-09-09T12:00:00-00:00")]
    [InlineData("2026-09-09T12:00:00+0000")]
    [InlineData("2026-09-09T12:00:00.Z")]
    [InlineData("2026-02-30T12:00:00Z")]
    [InlineData("2026-09-09T24:00:00Z")]
    [InlineData("2026-09-09T12:00:60Z")]
    [InlineData("2026-09-09T12:00:00Z ")]
    [InlineData("2026-09-09T12:00:00Z\n")]
    public void NonDatetimeAndMalformedTimestampsAreRejected(string timestamp)
        => Assert.Throws<InvalidDataException>(() => MessageContent.FromCbor(SourceRecord(timestamp)));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cid")]
    [InlineData("bafyreidvmsh4woms3l3flgzzn6763qlrl7hrirx2hbzbnxs2oxyhtfdegyextra")]
    [InlineData("bafyreidvmsh4woms3l3flgzzn6763qlrl7hrirx2hbzbnxs2oxyhtfdeg")]
    public void MalformedCidCannotRemainAnUnresolvedReply(string cid)
        => Assert.Throws<InvalidDataException>(() => MessageContent.FromCbor(SourceRecord("2026-09-09T12:00:00Z", new(ReplyUri, cid))));

    [Fact]
    public void TrailingCidBytesAcceptedByThePinnedSdkAreRejectedByTheMessageBoundary()
    {
        var withTrailingByte = ATCid.FromBytes([.. new ATCid(ReplyCid).ToBytes(), 0]).Value;
        Assert.True(new ATCid(withTrailingByte).IsAtProtoBlessedFormat);
        Assert.Throws<InvalidDataException>(() => MessageContent.FromCbor(SourceRecord("2026-09-09T12:00:00Z", new(ReplyUri, withTrailingByte))));
    }

    [Fact]
    public void NoncanonicalCidPaddingCannotCreateAnUnresolvableAlias()
    {
        var alternatePadding = ReplyCid[..^1] + "z";
        Assert.Equal(new ATCid(ReplyCid).Hash, new ATCid(alternatePadding).Hash);
        Assert.Throws<InvalidDataException>(() => MessageContent.FromCbor(SourceRecord("2026-09-09T12:00:00Z", new(ReplyUri, alternatePadding))));
    }

    [Fact]
    public void UnsupportedReplyCollectionCannotBecomeAnUnresolvedDependency()
    {
        var uri = ReplyUri.Replace("/local.tangent.message/", "/local.tangent.other/", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => MessageContent.FromCbor(SourceRecord("2026-09-09T12:00:00Z", new(uri, ReplyCid))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.test/message")]
    [InlineData("at://did:plc:yblwluenzocgiqpmu4ip7six/space/local.tangent.room/tangent-workshop")]
    [InlineData("at://did:plc:yblwluenzocgiqpmu4ip7six/space/local.tangent.room/tangent-workshop/not-a-did/local.tangent.message/one")]
    [InlineData("at://did:plc:yblwluenzocgiqpmu4ip7six/space/local.tangent.room/tangent-workshop/did:plc:mgxxqowf6nnd3btckqjq573l/local.tangent.message/..")]
    [InlineData("at://did:plc:yblwluenzocgiqpmu4ip7six/space/local.tangent.room/tangent-workshop/did:plc:mgxxqowf6nnd3btckqjq573l/local.tangent.message/one/extra")]
    [InlineData("at://did:plc:yblwluenzocgiqpmu4ip7six/space/local.tangent.room/tangent-workshop/did:plc:mgxxqowf6nnd3btckqjq573l/local.tangent.message/one?query=yes")]
    [InlineData("at://did:plc:yblwluenzocgiqpmu4ip7six/space/local.tangent.room/tangent-workshop/did:plc:mgxxqowf6nnd3btckqjq573l/local.tangent.message/one#fragment")]
    [InlineData("at://did:plc:yblwluenzocgiqpmu4ip7six/space/local.tangent.room/tangent-workshop/did:plc:mgxxqowf6nnd3btckqjq573l/local.tangent.message/percent%2Fkey")]
    public void MalformedSameRoomSourceUrisAreRejectedBeforeDeferral(string uri)
        => Assert.Throws<InvalidDataException>(() => MessageContent.FromCbor(SourceRecord("2026-09-09T12:00:00Z", new(uri, ReplyCid))));

    private static byte[] SourceRecord(string timestamp, SourceReference? reply = null)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(reply is null ? 3 : 4);
        writer.WriteTextString("text"); writer.WriteTextString("A source message");
        writer.WriteTextString("$type"); writer.WriteTextString(SpacesOptions.Collection);
        if (reply is not null)
        {
            writer.WriteTextString("replyTo"); writer.WriteStartMap(2);
            writer.WriteTextString("cid"); writer.WriteTextString(reply.Cid);
            writer.WriteTextString("uri"); writer.WriteTextString(reply.Uri);
            writer.WriteEndMap();
        }
        writer.WriteTextString("createdAt"); writer.WriteTextString(timestamp);
        writer.WriteEndMap();
        return writer.Encode();
    }

    [Fact]
    public void OperationIdentityIsScopedToParticipantAndRoom()
    {
        Assert.Equal(WriteIntent.Key("a", "room", "retry"), WriteIntent.Key("a", "room", "retry"));
        Assert.NotEqual(WriteIntent.Key("a", "room", "retry"), WriteIntent.Key("b", "room", "retry"));
        Assert.NotEqual(WriteIntent.Key("a", "room", "retry"), WriteIntent.Key("a", "elsewhere", "retry"));
        Assert.NotEqual(SourceDecision.Key("room", "uri", "old"), SourceDecision.Key("room", "uri", "new"));
    }

    private static byte[] Record(string text, string type, DateTimeOffset created, string? forgedAuthor = null)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(forgedAuthor is null ? 3 : 4);
        writer.WriteTextString("text"); writer.WriteTextString(text);
        writer.WriteTextString("$type"); writer.WriteTextString(type);
        if (forgedAuthor is not null) { writer.WriteTextString("author"); writer.WriteTextString(forgedAuthor); }
        writer.WriteTextString("createdAt"); writer.WriteTextString(created.ToString("O"));
        writer.WriteEndMap();
        return writer.Encode();
    }
}
