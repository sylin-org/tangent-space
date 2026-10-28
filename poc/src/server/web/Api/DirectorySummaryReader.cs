using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using AngleSharp.Dom;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Tangent.Community;
using Tangent.Activity;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Media;

namespace Tangent.Api;

/// <summary>Bounded directory previews over currently readable Topics and live Posts.</summary>
public sealed class DirectorySummaryReader(TangentGovernance tangents, TopicGovernance topics,
    ParticipantDirectory directory, ParticipantProfiles profiles)
{
    private const int ParticipantSample = 128;
    private static readonly QueryDefinition newestInTopic = new()
    {
        Sort = SortSpecParser.ParseStrict<Post>("-Sequence,-Id"),
        CountStrategy = CountStrategy.Exact
    };
    private static readonly QueryDefinition newestAcrossTopics = new()
    {
        Sort = SortSpecParser.ParseStrict<Post>("-AcceptedAt,-Id"),
        CountStrategy = CountStrategy.Exact
    };

    public async Task<TangentDescription?> DescribeTangent(string? actor, string key, CancellationToken ct)
    {
        var description = await tangents.Describe(actor, key, ct);
        return description is null ? null : await EnrichTangent(actor, description, ct);
    }

    public async Task<TangentsResponse> ListTangents(string? actor, int page, int topicPage, CancellationToken ct)
    {
        var listing = await tangents.List(actor, page, topicPage, ct);
        var enriched = new List<TangentDescription>(listing.Tangents.Count);
        foreach (var tangent in listing.Tangents)
            if (await EnrichTangent(actor, tangent, ct) is { } current) enriched.Add(current);
        return listing with { Tangents = enriched };
    }

    public async Task<TangentTopicDirectory> ListTopics(string? actor, string tangentKey, int page, CancellationToken ct)
    {
        var listing = await topics.ListForTangent(actor, tangentKey, page, ct);
        var enriched = new List<TopicDescription>(listing.Topics.Count);
        foreach (var topic in listing.Topics)
            if (await EnrichTopic(actor, topic, ct) is { } current) enriched.Add(current);
        return listing with { Topics = enriched };
    }

    public async Task<TopicDescription?> DescribeTopic(string? actor, string tangentKey, string topicKey, CancellationToken ct)
    {
        var topic = await topics.Describe(actor, topicKey, ct);
        return topic is null || topic.TangentKey != tangentKey ? null : await EnrichTopic(actor, topic, ct);
    }

    private async Task<TangentDescription?> EnrichTangent(string? actor, TangentDescription tangent, CancellationToken ct)
    {
        var slice = await tangents.ReadableTopicsForSummary(actor, tangent.Key, ct);
        var readable = slice.Topics;
        var ids = readable.Select(topic => topic.Key).ToArray();
        var postCount = 0L;
        DirectoryPost? latest = null;
        if (ids.Length > 0)
        {
            using var fresh = EntityContext.NoCache();
            // The explicit IN filter stays native on the pinned Koan providers.
            var filter = Filter.All(Filter.In(nameof(Post.TopicKey), ids),
                LinqFilterCompiler.Compile<Post>(post => post.Sequence > 0 && post.OfPostId == null && !post.Removed));
            var result = await Post.QueryWithCount(post => true,
                newestAcrossTopics.Where(filter).WithPagination(1, 1), ct);
            postCount = result.TotalCount;
            var post = result.Items.FirstOrDefault();
            if (post is not null)
            {
                var topic = readable.First(topic => topic.Key == post.TopicKey);
                latest = await Present(post, tangent.Key, topic.Title, ct);
            }
        }
        var stillVisible = await tangents.Describe(actor, tangent.Key, ct);
        var current = await tangents.ReadableTopicsForSummary(actor, tangent.Key, ct);
        if (stillVisible is null || current.Incomplete != slice.Incomplete
            || !current.Topics.Select(topic => topic.Key).SequenceEqual(ids))
            return null;
        var currentTopics = current.Topics.ToDictionary(topic => topic.Key, StringComparer.Ordinal);
        var visibleTopics = new List<TopicDescription>(tangent.Topics.Count);
        foreach (var topic in tangent.Topics)
        {
            var visible = currentTopics.GetValueOrDefault(topic.Key) ?? await topics.Describe(actor, topic.Key, ct);
            if (visible?.TangentKey == tangent.Key) visibleTopics.Add(visible);
        }
        if (latest is not null) latest = latest with { TopicTitle = currentTopics[latest.TopicKey].Title };
        var recent = new List<DirectoryTopicLink>();
        foreach (var topic in current.Topics.OrderByDescending(topic => topic.CreatedAt).ThenByDescending(topic => topic.Key).Take(3))
            if (await EnrichTopic(actor, topic, ct) is { CanRead: true } preview)
                recent.Add(new DirectoryTopicLink(preview.Key, preview.Title, preview.Summary));
        var summary = new TangentDirectorySummary(new(readable.Count, slice.Incomplete),
            new(postCount, slice.Incomplete), recent, latest, slice.Incomplete);
        return stillVisible with { Topics = visibleTopics, TopicsTruncated = tangent.TopicsTruncated,
            NextTopicsPage = tangent.NextTopicsPage, TopicsIncomplete = tangent.TopicsIncomplete, Summary = summary };
    }

    private async Task<TopicDescription?> EnrichTopic(string? actor, TopicDescription topic, CancellationToken ct)
    {
        if (!topic.CanRead) return await topics.Describe(actor, topic.Key, ct);
        using var fresh = EntityContext.NoCache();
        var posts = await Post.QueryWithCount(post => post.TopicKey == topic.Key && post.Sequence > 0
                && post.OfPostId == null && !post.Removed,
            newestInTopic.WithPagination(1, ParticipantSample), ct);
        var participantCount = posts.Items.Select(post => post.AuthorParticipantId)
            .Distinct(StringComparer.Ordinal).LongCount();
        var opening = (await Post.Query(post => post.TopicKey == topic.Key && post.Sequence == 1
            && post.OfPostId == null, newestInTopic.WithPagination(1, 1), ct)).FirstOrDefault();
        var starter = await PresentParticipant(topic.CreatorParticipantId, ct);
        var latest = posts.Items.Count == 0 ? null : await Present(posts.Items[0], topic.TangentKey, topic.Title, ct);
        ArticleDirectoryPreview? article = null;
        if (topic.Presentation == TopicPresentation.Article)
        {
            if (opening?.Content.ReplyTo is null && opening is not null)
            {
                var cover = opening.Removed ? null : await CoverImage(opening, ct);
                var minutes = opening.Removed ? 0 : ReadingMinutes(opening.Content);
                article = new ArticleDirectoryPreview(opening.Id, cover,
                    await PresentParticipant(opening.AuthorParticipantId, ct), opening.AcceptedAt, minutes, opening.Removed, opening.Removed ? "" : Excerpt(opening.Content, article: true));
            }
        }
        var stillVisible = await topics.Describe(actor, topic.Key, ct);
        if (stillVisible is null || stillVisible.TangentKey != topic.TangentKey) return null;
        if (!stillVisible.CanRead) return stillVisible;
        if (latest is not null) latest = latest with { TopicTitle = stillVisible.Title };
        return stillVisible with { Summary = new TopicDirectorySummary(new(posts.TotalCount),
            new(participantCount, posts.TotalCount > posts.Items.Count), starter,
            topic.CreatedAt ?? default, latest,
            stillVisible.Presentation == TopicPresentation.Article ? article : null,
            actor is null ? 0 : (await TopicAttention.Get(TopicAttention.IdOf(topic.Key, actor), ct))?.UnreadCount ?? 0,
            actor is not null && (await WatchSetting.Get(WatchSetting.Key(actor, topic.Key), ct)) is { Mode: not WatchMode.None },
            opening is { Removed: false } ? await Present(opening, topic.TangentKey, topic.Title, ct) : null,
            new DirectoryCount(Math.Max(0, posts.TotalCount - (opening is { Removed: false } ? 1 : 0)))) };
    }

    private static int ReadingMinutes(PostContent content)
        => Math.Max(1, (PostMarkup.PlainText(content).Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries).Length + 219) / 220);

    private static async Task<string?> CoverImage(Post opening, CancellationToken ct)
    {
        if (opening.Content.Format != PostContent.Markdown) return null;
        var html = PostMarkup.Html(opening.Content);
        if (html is null) return null;
        var document = new HtmlParser().ParseDocument("<body>" + html + "</body>");
        foreach (var image in document.QuerySelectorAll("img[src]"))
        {
            var src = image.GetAttribute("src");
            if (src is null || !src.StartsWith("/media/", StringComparison.Ordinal)) continue;
            var id = src[7..];
            if (id.Length != 32 || id.Any(character => !char.IsAsciiHexDigit(character))) continue;
            var asset = await PostMediaAsset.Get(id, ct);
            if (asset?.PostId == opening.Id && asset.TopicKey == opening.TopicKey
                && asset.OwnerParticipantId == opening.AuthorParticipantId)
                return src;
        }
        return null;
    }

    private async Task<DirectoryPost> Present(Post post, string tangentKey, string title, CancellationToken ct)
        => new(post.Id, post.TopicKey, title,
            $"/t/{Uri.EscapeDataString(tangentKey)}/{Uri.EscapeDataString(post.Id)}",
            Excerpt(post.Content), post.AcceptedAt,
            await PresentParticipant(post.AuthorParticipantId, ct));

    private async Task<DirectoryParticipant> PresentParticipant(string id, CancellationToken ct)
    {
        var participant = await Participant.Get(id, ct);
        var profile = await profiles.Read(id, ct);
        var identity = await directory.PerennialValue(id, ct);
        var label = profile.DisplayName ?? profile.Handle ?? "Participant";
        return new DirectoryParticipant(id, label, profile.Handle, profile.Avatar,
            (participant?.Classification ?? ParticipantClassification.Undeclared).ToString(),
            "/u/" + Uri.EscapeDataString(identity));
    }

    private static string Excerpt(PostContent content, bool article = false)
    {
        var plain = PostMarkup.PlainText(content);
        if (article && content.Format == PostContent.Markdown)
        {
            var document = new HtmlParser().ParseDocument("<body>" + PostMarkup.Html(content) + "</body>");
            foreach (var decoration in document.QuerySelectorAll("figure, img, h1, h2, h3, h4, h5, h6")) decoration.Remove();
            plain = string.Join(' ', document.QuerySelectorAll("p, li, pre").Where(element => element.ParentElement?.Closest("p, li, pre") is null).Select(element => element.TextContent));
        }
        plain = Regex.Replace(Regex.Replace(plain, "<[^>]*>", " "), @"\s+", " ").Trim();
        return plain.Length <= 160 ? plain : plain[..160].TrimEnd() + "…";
    }
}
