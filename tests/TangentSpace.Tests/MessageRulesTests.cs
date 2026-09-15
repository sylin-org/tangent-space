using System.Text.Json;
using TangentSpace.Conversation;
using Xunit;

namespace TangentSpace.Tests;

public sealed class MessageRulesTests
{
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
    public void ContentClockDoesNotDetermineDecisionClockOrOrdering()
    {
        var decision = new SourceDecision { Id = "source", Accepted = true, DecidedAt = DateTimeOffset.UtcNow,
            Sequence = 42, Content = new MessageContent("late", DateTimeOffset.UnixEpoch, null),
            AuthorParticipantId = "11111111111111111111111111111111" };
        var projected = Message.Project(decision);
        Assert.Equal(DateTimeOffset.UnixEpoch, projected.Content.CreatedAt);
        Assert.Equal(decision.DecidedAt, projected.AcceptedAt);
        Assert.Equal(42, projected.Sequence);
        Assert.Equal(decision.AuthorParticipantId, projected.AuthorParticipantId);
    }

    [Fact]
    public void Decisions_are_keyed_by_Topic_source_and_version()
        => Assert.NotEqual(SourceDecision.Key("room", "uri", "old"), SourceDecision.Key("room", "uri", "new"));
}
