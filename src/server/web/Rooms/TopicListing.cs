namespace TangentSpace.Rooms;

public sealed record TopicListing(IReadOnlyList<TopicDescription> Rooms, int Page, int? NextPage, DateTimeOffset CheckedAt);
