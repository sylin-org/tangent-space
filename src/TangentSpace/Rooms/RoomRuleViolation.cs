namespace TangentSpace.Rooms;

public sealed class RoomRuleViolation(RoomDenial denial, string message) : InvalidOperationException(message)
{
    public RoomDenial Denial { get; } = denial;
}
