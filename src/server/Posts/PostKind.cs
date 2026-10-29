namespace Tangent.Posts;

/// <summary>What a Topic is: a conversation, or an article that opens with a published piece. Set on the root and read
/// from it; a reply stays at the default.</summary>
public enum PostKind
{
    Topic,
    Article
}
