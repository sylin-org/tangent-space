namespace Tangent.Access;

/// <summary>Typed Topic/Post capabilities shared by HTTP and MCP permission projections.</summary>
public enum TopicCapability
{
    Read,
    Reply,
    ManageTopic,
    ManageParticipants,
    AppointManagers,
    EditOwnPost,
    DeleteOwnPost,
    RemovePost,
    ReportPost
}
