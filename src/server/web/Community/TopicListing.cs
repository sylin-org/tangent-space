namespace Tangent.Community;

public sealed record TopicListing(IReadOnlyList<TopicDescription> Topics, int Page, int? NextPage, DateTimeOffset CheckedAt);
