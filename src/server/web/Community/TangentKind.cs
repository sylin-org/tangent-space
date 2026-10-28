namespace Tangent.Community;

public static class TangentKind
{
    public const string Discussions = "discussions";
    public const string Articles = "articles";

    public static string Check(string? value) => value switch
    {
        null or Discussions => Discussions,
        Articles => Articles,
        _ => throw new TangentRuleViolation(TangentDenial.InvalidInput, "Choose Discussions or Articles.")
    };

    public static string TopicPresentation(string kind)
        => kind == Articles ? global::Tangent.Community.TopicPresentation.Article : global::Tangent.Community.TopicPresentation.Conversation;
}
