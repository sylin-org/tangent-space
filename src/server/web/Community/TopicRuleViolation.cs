namespace Tangent.Community;

public sealed class TopicRuleViolation(TopicDenial denial, string message) : InvalidOperationException(message)
{
    public TopicDenial Denial { get; } = denial;
}
