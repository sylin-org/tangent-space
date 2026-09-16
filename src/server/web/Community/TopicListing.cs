namespace Tangent.Community;

public sealed record TopicListing(IReadOnlyList<TopicDescription> Rooms, int Page, int? NextPage, DateTimeOffset CheckedAt);
