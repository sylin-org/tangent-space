using Tangent.Community;
using Tangent.Spaces;
using Tangent.Access;
namespace Tangent.Api;

/// <summary>The deliberately small, allowlisted representation safe for anonymous caches and documents.</summary>
public sealed record PublicTopicDescription(string Key, string TangentKey, string Title, string Topic,
    TopicReadAudience ReadAudience, string Path, string Presentation = TopicPresentation.Conversation, string SpaceName = "Tangent Space", string SpaceAccent = "gold")
{
    internal static PublicTopicDescription From(Topic topic, Community.Tangent tangent, Space? space = null)
        => new(topic.Id, topic.TangentKey, topic.Title, topic.Description, topic.ReadAudience,
            $"/t/{Uri.EscapeDataString(topic.TangentKey)}/topics/{Uri.EscapeDataString(topic.Id)}", TangentKind.TopicPresentation(tangent.Kind), PublicSpaceName(space), PublicSpaceAccent(space));
    internal static string PublicSpaceName(Space? space) => space?.Access.ResolveServer().See?.Contains(AccessTokens.Everyone) == true ? space.Name : "Tangent Space";
    internal static string PublicSpaceAccent(Space? space) => space?.Access.ResolveServer().See?.Contains(AccessTokens.Everyone) == true ? space.Accent : "gold";
}
