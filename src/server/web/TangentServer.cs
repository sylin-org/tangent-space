using TangentSpace.Activity;
using TangentSpace.AtProtocol;
using TangentSpace.Communities;
using TangentSpace.Hosting;
using TangentSpace.Rooms;
using TangentSpace.Site;
using TangentSpace.Conversation;

namespace TangentSpace;

/// <summary>The singleton application hub. Consumers enter the same domain operations;
/// permission checks, source confirmation, commits and notifications remain in those services.
/// No current actor, request, entity session or transaction is retained here.</summary>
public sealed class TangentServer(
    ServerGovernance site, TangentGovernance tangents, CompanionGovernance participants,
    RoomGovernance topics, ConversationService posts, ActivityService activity,
    SpacesService source, SourceReadiness readiness, TangentSpace.Participants.ParticipantProfiles profiles)
{
    public TangentSpace.Participants.ParticipantProfiles Profiles { get; } = profiles;
    public ServerGovernance Site { get; } = site;
    public TangentGovernance Tangents { get; } = tangents;
    public CompanionGovernance Participants { get; } = participants;
    public RoomGovernance Topics { get; } = topics;
    public ConversationService Posts { get; } = posts;
    public ActivityService Activity { get; } = activity;
    public SpacesService Source { get; } = source;
    public SourceReadiness Readiness { get; } = readiness;

}
