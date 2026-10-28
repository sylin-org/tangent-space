using Tangent.Access;

namespace Tangent.Community;

/// <summary>Anonymous history visibility and consent use the same effective access rule.</summary>
public static class PublicHistory
{
    public static bool IsPublic(TopicReadAudience audience, AccessMap topicAccess, AccessMap? tangentAccess)
        => tangentAccess is not null && audience == TopicReadAudience.Public
            && topicAccess.ResolveTopic(tangentAccess).See!
                .Contains(AccessTokens.Everyone, StringComparer.Ordinal);

    public static void RequireConsent(bool wasPublic, bool willBePublic, bool publishExistingHistory)
    {
        if (!wasPublic && willBePublic && !publishExistingHistory)
            throw new TopicRuleViolation(TopicDenial.HistoryPublicationRequired,
                "Confirm that existing conversation history affected by this change will become publicly readable.");
    }
}
