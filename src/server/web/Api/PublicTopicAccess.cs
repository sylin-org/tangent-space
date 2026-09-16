using Koan.Web.Authorization;
using Tangent.Community;

namespace Tangent.Api;

/// <summary>Koan-native row visibility for the anonymous Topic read surface.</summary>
public sealed class PublicTopicAccess : EntityAccess<Topic>
{
    protected override ActionGate ReadGate => Gate.Anyone;
    protected override ActionGate WriteGate => Gate.HasClaim("tangent:surface", "internal-domain-command");
    protected override ActionGate RemoveGate => Gate.HasClaim("tangent:surface", "internal-domain-command");

    public override IAccessFilter<Topic> Constrain(IAccessFilter<Topic> q, AccessAction action)
        => action == AccessAction.Read
            ? q.Where(topic => topic.ReadAudience == TopicReadAudience.Public)
            : action is AccessAction.Update or AccessAction.Delete ? q.Where(topic => false) : q;

    internal static bool IsPublic(Topic topic) => topic.ReadAudience == TopicReadAudience.Public;
}
