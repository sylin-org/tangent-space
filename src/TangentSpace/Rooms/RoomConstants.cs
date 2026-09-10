namespace TangentSpace.Rooms;

internal static class RoomConstants
{
    public const int MaximumKeyLength = 64;
    public const int MaximumTitleLength = 120;
    public const int MaximumTopicLength = 2000;
    public const int PageSize = 100;
    public const int MaximumPage = 10000;
    public const string AdministrationTransaction = "tangent-room-administration";
    public const string AcceptanceTransaction = "tangent-room-policy-operation";
}
