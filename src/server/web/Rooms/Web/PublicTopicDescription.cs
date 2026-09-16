namespace TangentSpace.Rooms.Web;

/// <summary>The deliberately small, allowlisted representation safe for anonymous caches and documents.</summary>
public sealed record PublicTopicDescription(string Key, string TangentKey, string Title, string Topic,
    TopicReadAudience ReadAudience, string Path)
{
    internal static PublicTopicDescription From(Topic topic)
        => new(topic.Id, topic.TangentKey, topic.Title, topic.Description, topic.ReadAudience,
            $"/t/{Uri.EscapeDataString(topic.TangentKey)}/topics/{Uri.EscapeDataString(topic.Id)}");
}
