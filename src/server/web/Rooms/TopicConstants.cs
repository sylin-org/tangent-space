namespace TangentSpace.Rooms;

internal static class TopicConstants
{
    public const int MaximumKeyLength = 64;
    public const int MaximumTitleLength = 120;
    public const int MaximumDescriptionLength = 2000;
    public const int PageSize = 100;
    public const int MaximumPage = 10000;
    public const string AdministrationTransaction = "tangent-topic-administration";
    public const string AcceptanceTransaction = "tangent-topic-policy-operation";
}
