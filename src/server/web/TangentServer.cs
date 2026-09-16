using TangentSpace.Activity;
using TangentSpace.Communities;
using TangentSpace.Hosting;
using TangentSpace.Rooms;
using TangentSpace.Site;
using TangentSpace.Conversation;

namespace TangentSpace;

/// <summary>The singleton application hub. Consumers enter the same domain operations;
/// permission checks, commits and notifications remain in those services.
/// No current actor, request, entity session or transaction is retained here.</summary>
public sealed class TangentServer(
    ServerGovernance space, TangentGovernance tangents, ParticipantGovernance participants,
    TopicGovernance topics, ConversationService posts, ActivityService activity,
    TangentSpace.Participants.ParticipantProfiles profiles,
    TangentSpace.Participants.ParticipantDirectory directory, LiveSessions live)
{
    public TangentSpace.Participants.ParticipantProfiles Profiles { get; } = profiles;
    public ServerGovernance Space { get; } = space;
    public TangentGovernance Tangents { get; } = tangents;
    public ParticipantGovernance Participants { get; } = participants;
    public TopicGovernance Topics { get; } = topics;
    public ConversationService Posts { get; } = posts;
    public ActivityService Activity { get; } = activity;
    public TangentSpace.Participants.ParticipantDirectory Directory { get; } = directory;
    public LiveSessions Live { get; } = live;

}
