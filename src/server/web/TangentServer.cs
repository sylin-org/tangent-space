using Tangent.Activity;
using Tangent.Community;
using Tangent.Api;
using Tangent.Community;
using Tangent.Stewardship;
using Tangent.Spaces;
using Tangent.Conversation;
using Tangent.Activity;
using Tangent.Identity;

namespace Tangent;

/// <summary>The singleton application hub. Consumers enter the same domain operations;
/// permission checks, commits and notifications remain in those services.
/// No current actor, request, entity session or transaction is retained here.</summary>
public sealed class TangentServer(
    ServerGovernance space, TangentGovernance tangents, ParticipantGovernance participants,
    TopicGovernance topics, ConversationService posts, ActivityService activity,
    ParticipantProfiles profiles,
    ParticipantDirectory directory, LiveSessions live)
{
    public ParticipantProfiles Profiles { get; } = profiles;
    public ServerGovernance Space { get; } = space;
    public TangentGovernance Tangents { get; } = tangents;
    public ParticipantGovernance Participants { get; } = participants;
    public TopicGovernance Topics { get; } = topics;
    public ConversationService Posts { get; } = posts;
    public ActivityService Activity { get; } = activity;
    public ParticipantDirectory Directory { get; } = directory;
    public LiveSessions Live { get; } = live;

}
