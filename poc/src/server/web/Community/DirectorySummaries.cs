namespace Tangent.Community;

public sealed record DirectoryCount(long Value, bool AtLeast = false);

public sealed record DirectoryParticipant(string Id, string DisplayName, string? Handle,
    string? Avatar, string Classification, string ProfileUrl);

public sealed record DirectoryPost(string Id, string TopicKey, string TopicTitle, string Url,
    string Excerpt, DateTimeOffset CreatedAt, DirectoryParticipant Author);

public sealed record DirectoryTopicLink(string Key, string Title, TopicDirectorySummary? Summary = null);

public sealed record TangentDirectorySummary(DirectoryCount Topics, DirectoryCount Posts,
    IReadOnlyList<DirectoryTopicLink> RecentTopics, DirectoryPost? LatestPost, bool Incomplete);

public sealed record TopicDirectorySummary(DirectoryCount Posts, DirectoryCount Participants,
    DirectoryParticipant? Starter, DateTimeOffset CreatedAt, DirectoryPost? LatestPost,
    ArticleDirectoryPreview? Article = null, int Unread = 0, bool Following = false,
    DirectoryPost? OpeningPost = null, DirectoryCount? Replies = null);

public sealed record ArticleDirectoryPreview(string OpeningPostId, string? CoverImageUrl,
    DirectoryParticipant Author, DateTimeOffset CreatedAt, int ReadingMinutes, bool Removed, string Excerpt = "");
