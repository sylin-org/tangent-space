using TangentSpace.Rooms;

namespace TangentSpace.Conversation;

/// <summary>Conversation storage scope for new Topics. Local is the standalone default:
/// entity-backed Posts with verified DID identity and no Atmosphere dependency. Spaces keeps
/// the authority-backed source pipeline (ADR 0006).</summary>
public sealed class ConversationOptions
{
    public const string Configuration = "Tangent:Conversation";
    public string Storage { get; set; } = "Local";

    public bool LocalByDefault => string.Equals(Storage, "Local", StringComparison.OrdinalIgnoreCase);

    public RoomSpaceState NewTopicState => LocalByDefault ? RoomSpaceState.Local : RoomSpaceState.Pending;
}
