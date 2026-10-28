using System.Globalization;
using System.Net;
using System.Text;
using AngleSharp.Html.Parser;
using AngleSharp.Dom;
using Tangent.Community;
using Tangent.Conversation;

namespace Tangent.Api;

/// <summary>Escaped, dependency-free public documents built from the shared public projection.</summary>
internal static class PublicPageRenderer
{
    internal static string WithWorkspace(string html, string? workspaceHtml)
    {
        var assets = new StringBuilder();
        if (!string.IsNullOrEmpty(workspaceHtml))
        {
            var workspace = new HtmlParser().ParseDocument(workspaceHtml);
            static bool LocalAsset(string? value) => value is not null && value.StartsWith("/assets/", StringComparison.Ordinal)
                && !value.Contains('\\') && !value.Contains("..", StringComparison.Ordinal);
            foreach (var style in workspace.QuerySelectorAll("link[rel=stylesheet][href]"))
                if (LocalAsset(style.GetAttribute("href")))
                    assets.Append("<link rel=\"stylesheet\" href=\"").Append(E(style.GetAttribute("href")))
                        .Append("\" media=\"not all\" data-workspace-style>");
            foreach (var script in workspace.QuerySelectorAll("script[type=module][src]"))
                if (LocalAsset(script.GetAttribute("src")))
                    assets.Append("<script type=\"module\" src=\"").Append(E(script.GetAttribute("src"))).Append("\"></script>");
        }
        return html.Replace("</head>", assets + "</head>", StringComparison.Ordinal)
            .Replace("<body>", "<body><div id=\"public-document\">", StringComparison.Ordinal)
            .Replace("</body>", "</div><div id=\"app\" hidden aria-hidden=\"true\"></div></body>", StringComparison.Ordinal);
    }

    private static void AppendMasthead(StringBuilder html, string spaceName, string canonicalPath)
    {
        html.Append("<header class=\"masthead\"><a class=\"brand\" href=\"/\"><span class=\"mark\" aria-hidden=\"true\">t</span>")
            .Append(E(spaceName)).Append("</a><form class=\"public-search\" action=\"/search\" method=\"get\" role=\"search\">")
            .Append("<label class=\"visually-hidden\" for=\"public-search-query\">Search this Space</label><input id=\"public-search-query\" name=\"q\" type=\"search\" placeholder=\"Search this Space\" required><button type=\"submit\">Search</button></form>")
            .Append("<a class=\"sign-in\" data-public-sign-in href=\"/sign-in/?return=").Append(E(Uri.EscapeDataString(canonicalPath)))
            .Append("\">Sign in to take part</a></header>");
    }
    internal static string ArticleCollection(TangentDescription tangent, TangentTopicDirectory listing, Tangent.Spaces.Space? space = null)
    {
        var spaceName=PublicTopicDescription.PublicSpaceName(space);
        var spaceAccent=PublicTopicDescription.PublicSpaceAccent(space);
        var basePath = "/t/" + Uri.EscapeDataString(tangent.Key) + "/topics";
        var canonical = basePath + (listing.Page > 1 ? "?page=" + listing.Page.ToString(CultureInfo.InvariantCulture) : "");
        var html = new StringBuilder(4096);
        html.Append("<!doctype html><html lang=\"en\" data-accent=\"").Append(E(spaceAccent)).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<meta name=\"robots\" content=\"noindex,follow\">")
            .Append("<link rel=\"canonical\" href=\"").Append(E(canonical)).Append("\">")
            .Append("<link rel=\"icon\" type=\"image/svg+xml\" href=\"/favicon.svg\">")
            .Append("<link rel=\"stylesheet\" data-public-style href=\"/public.css?v=20260929-workspace\">")
            .Append("<script src=\"/public-fragment.js\" defer></script><title>").Append(E(tangent.Name)).Append(" · Articles · ").Append(E(spaceName)).Append("</title></head><body>")
            .Append("<a class=\"skip\" href=\"#article-collection\">Skip to articles</a>");
        AppendMasthead(html, spaceName, canonical);
        html.Append("<main><nav class=\"breadcrumbs\" aria-label=\"Breadcrumb\"><a href=\"/\">Tangents</a><span aria-hidden=\"true\">/</span><span aria-current=\"page\">")
            .Append(E(tangent.Name)).Append("</span></nav>")
            .Append("<header class=\"topic-hero\"><p class=\"eyebrow\">Articles</p><h1>")
            .Append(E(tangent.Name)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(tangent.Description))
            html.Append("<p class=\"topic-intro\">").Append(E(tangent.Description)).Append("</p>");
        html.Append("</header><section id=\"article-collection\" class=\"article-collection\" aria-label=\"Articles\">")
            .Append("<div class=\"article-grid\">");
        foreach (var topic in listing.Topics)
        {
            var href = "/t/" + Uri.EscapeDataString(tangent.Key) + "/topics/" + Uri.EscapeDataString(topic.Key);
            html.Append("<article class=\"article-card\"><a href=\"").Append(E(href)).Append("\">")
                .Append("<div class=\"article-card-cover\">");
            if (topic.Summary?.Article?.CoverImageUrl is { } cover)
                html.Append("<img src=\"").Append(E(cover)).Append("\" alt=\"\" loading=\"lazy\" decoding=\"async\">");
            else html.Append("<span class=\"article-card-fallback\" aria-hidden=\"true\"><span>✦</span><small>TANGENT SPACE</small></span>");
            html.Append("</div><h2 class=\"article-card-title\">").Append(E(topic.Title)).Append("</h2>")
                .Append("<p class=\"article-card-meta\">");
            if (topic.Summary?.Article is { } article)
            {
                html.Append(E(article.Author.DisplayName)).Append(" · <time datetime=\"")
                    .Append(E(article.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)))
                    .Append("\">").Append(E(article.CreatedAt.ToUniversalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)))
                    .Append("</time>");
                if (article.Removed)
                    html.Append(" · Article opening removed");
                else
                    html.Append(" · ").Append(article.ReadingMinutes.ToString(CultureInfo.InvariantCulture)).Append(" min read");
            }
            else html.Append("Opening Post unavailable");
            html.Append("</p>");
            if(topic.Summary?.Article?.Excerpt is { Length: > 0 } excerpt) html.Append("<p class=\"article-card-summary\">").Append(E(excerpt)).Append("</p>");
            html.Append("</a></article>");
        }
        html.Append("</div>");
        if (listing.Topics.Count == 0) html.Append("<p class=\"empty\">There are no public articles on this page.</p>");
        if (listing.Page > 1 || listing.NextPage is not null)
        {
            html.Append("<nav class=\"window-nav\" aria-label=\"Article pages\">");
            if (listing.Page > 1)
                html.Append("<a rel=\"prev\" href=\"").Append(E(basePath + (listing.Page == 2 ? "" : "?page=" + (listing.Page - 1))))
                    .Append("\">← Newer articles</a>");
            else html.Append("<span></span>");
            if (listing.NextPage is { } next)
                html.Append("<a rel=\"next\" href=\"").Append(E(basePath + "?page=" + next))
                    .Append("\">Older articles →</a>");
            html.Append("</nav>");
        }
        html.Append("</section></main><footer><p>A public collection on Tangent Space.</p>")
            .Append("<a href=\"https://github.com/sylin-org/tangent-space\">Run your own Tangent</a><fieldset class=\"appearance-switch\" aria-label=\"Appearance\"><legend>Appearance</legend><button type=\"button\" data-public-theme=\"light\">Light</button><button type=\"button\" data-public-theme=\"dark\">Dark</button><button type=\"button\" data-public-theme=\"system\">System</button></fieldset></footer></body></html>");
        return html.ToString();
    }

    internal static string Topic(PublicPostWindow window, string canonicalPath, string? publicOrigin = null, string? coverImageUrl = null)
        => Render(window, canonicalPath, anchorId: null, publicOrigin, coverImageUrl);

    internal static string Post(PublicPostDocument document, string canonicalPath, string? publicOrigin = null, string? coverImageUrl = null)
        => Render(document.Window, canonicalPath, document.AnchorId, publicOrigin, coverImageUrl);

    private static string Render(PublicPostWindow window, string canonicalPath, string? anchorId, string? publicOrigin, string? coverImageUrl)
    {
        var topic = window.Topic;
        var spaceName=topic.SpaceName;var spaceAccent=topic.SpaceAccent;
        var articleMode = topic.Presentation == TopicPresentation.Article;
        var title = anchorId is null || articleMode ? topic.Title : $"Post in {topic.Title}";
        var description = string.IsNullOrWhiteSpace(topic.Topic) ? $"A public Topic on Tangent Space: {topic.Title}"
            : topic.Topic.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (description.Length > 180) description = description[..177] + "…";
        var html = new StringBuilder(4096);
        html.Append("<!doctype html><html lang=\"en\" data-accent=\"").Append(E(spaceAccent)).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<meta name=\"robots\" content=\"noindex,follow\">")
            .Append("<meta name=\"description\" content=\"").Append(E(description)).Append("\">");
        if (articleMode && publicOrigin is not null)
        {
            html.Append("<meta property=\"og:type\" content=\"article\">")
                .Append("<meta property=\"og:title\" content=\"").Append(E(title)).Append("\">")
                .Append("<meta property=\"og:description\" content=\"").Append(E(description)).Append("\">")
                .Append("<meta property=\"og:url\" content=\"").Append(E(publicOrigin + topic.Path)).Append("\">");
            if (coverImageUrl is not null && window.OpeningPost is { Removed: false })
                html.Append("<meta property=\"og:image\" content=\"").Append(E(coverImageUrl)).Append("\">")
                    .Append("<meta name=\"twitter:card\" content=\"summary_large_image\">");
        }
        html.Append("<link rel=\"canonical\" href=\"").Append(E(canonicalPath)).Append("\">")
            .Append("<link rel=\"icon\" type=\"image/svg+xml\" href=\"/favicon.svg\">")
            .Append("<link rel=\"stylesheet\" data-public-style href=\"/public.css?v=20260929-workspace\">")
            .Append("<link rel=\"stylesheet\" href=\"/article-media.css?v=20260929\">")
            .Append("<script src=\"/public-fragment.js\" defer></script>")
            .Append("<title>").Append(E(title)).Append(" · ").Append(E(spaceName)).Append("</title></head><body>")
            .Append("<a class=\"skip\" href=\"#conversation\">Skip to conversation</a>");
        AppendMasthead(html, spaceName, canonicalPath);
        html.Append("<main>");
        if (articleMode)
            AppendArticle(html, window, anchorId);
        else
        {
            html.Append("<nav class=\"breadcrumbs\" aria-label=\"Breadcrumb\"><a href=\"/\">Tangents</a><span aria-hidden=\"true\">/</span>")
            .Append("<a href=\"/t/").Append(E(Uri.EscapeDataString(topic.TangentKey))).Append("/topics\">")
            .Append(E(topic.TangentKey)).Append("</a><span aria-hidden=\"true\">/</span>");
            if (anchorId is not null)
                html.Append("<a href=\"").Append(E(topic.Path)).Append("\">").Append(E(topic.Title)).Append("</a><span aria-hidden=\"true\">/</span><span aria-current=\"page\">Post</span>");
            else html.Append("<span aria-current=\"page\">").Append(E(topic.Title)).Append("</span>");
            html.Append("</nav><header class=\"topic-hero\"><p class=\"eyebrow\">Public Topic</p><h1>")
                .Append(E(title)).Append("</h1>");
            if (!string.IsNullOrWhiteSpace(topic.Topic)) html.Append("<p class=\"topic-intro\">").Append(E(topic.Topic)).Append("</p>");
            html.Append("<p class=\"public-note\"><span aria-hidden=\"true\">●</span> Public conversation · sign in to check reply access</p>");
            html.Append("</header>");
            html.Append("<section id=\"conversation\" class=\"conversation\" aria-label=\"Conversation\">");
            if (window.Posts.Count == 0) html.Append("<p class=\"empty\">There are no public posts in this window.</p>");
            foreach (var post in window.Posts) AppendPost(html, post, post.Id == anchorId);
            html.Append("</section>");
        }
        AppendNavigation(html, topic.Path, window);
        html.Append("</main><footer><p>A public conversation on Tangent Space.</p>")
            .Append("<a href=\"https://github.com/sylin-org/tangent-space\">Run your own Tangent</a><fieldset class=\"appearance-switch\" aria-label=\"Appearance\"><legend>Appearance</legend><button type=\"button\" data-public-theme=\"light\">Light</button><button type=\"button\" data-public-theme=\"dark\">Dark</button><button type=\"button\" data-public-theme=\"system\">System</button></fieldset></footer></body></html>");
        return html.ToString();
    }

    private static void AppendArticle(StringBuilder html, PublicPostWindow window, string? anchorId)
    {
        var topic = window.Topic;
        var opening = window.OpeningPost;
        var headings = new List<(string Id, string Title)>();
        var body = opening is { Removed: false, Format: PostContent.Markdown, Html: not null }
            ? ArticleBody(opening.Html, headings) : null;
        html.Append("<div class=\"article-layout\"><div class=\"article-main\"><header class=\"article-hero\">")
            .Append("<nav class=\"article-context\" aria-label=\"Breadcrumb\"><a href=\"/\">Tangents</a><span aria-hidden=\"true\">/</span>")
            .Append("<a href=\"/t/").Append(E(Uri.EscapeDataString(topic.TangentKey))).Append("/topics\">Articles</a></nav>")
            .Append("<h1>").Append(E(topic.Title)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(topic.Topic)) html.Append("<p class=\"topic-intro\">").Append(E(topic.Topic)).Append("</p>");
        html.Append("<div class=\"article-byline\">");
        if (opening is { Removed: false })
        {
            AppendAuthorMark(html, opening.Author);
            AppendAuthorName(html, opening);
            html.Append("<time datetime=\"")
                .Append(E(opening.AcceptedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))).Append("\">")
                .Append(E(opening.AcceptedAt.ToUniversalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)))
                .Append("</time><span>").Append(ReadingMinutes(opening.Text)).Append(" min read</span>")
                .Append("<span class=\"article-audience\" title=\"Anyone on the web can read this article\">Public</span>")
                .Append("<a class=\"article-permalink\" href=\"").Append(E(opening.Path)).Append("\" aria-label=\"Link to article\" title=\"Link to article\">")
                .Append("<svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" aria-hidden=\"true\"><path d=\"M10 13a5 5 0 0 0 7.1 0l2.1-2.1a5 5 0 0 0-7.1-7.1L10.9 5M14 11a5 5 0 0 0-7.1 0l-2.1 2.1a5 5 0 0 0 7.1 7.1l1.2-1.2\"/></svg></a>");
        }
        html.Append("<a class=\"article-discussion-jump\" href=\"#discussion\">Discussion ↓</a>");
        if (anchorId is not null && anchorId != opening?.Id)
            html.Append("<a class=\"article-target\" href=\"#post-").Append(E(anchorId))
                .Append("\">Read linked Post ↓</a>");
        html.Append("</div></header>");
        if (headings.Count > 0) AppendMobileOutline(html, headings);
        html.Append("<section id=\"article\" aria-label=\"Article\">");
        if (opening is null) html.Append("<p class=\"empty\">This article has no opening Post.</p>");
        else
        {
            html.Append("<article class=\"article-opening");
            if (opening.Id == anchorId) html.Append(" post-anchor");
            html.Append("\" id=\"post-").Append(E(opening.Id)).Append("\">")
                .Append("<div class=\"article-body article-media post-text");
            if (opening.Format == PostContent.Markdown) html.Append(" markdown");
            if (opening.Removed) html.Append(" removed");
            html.Append("\">").Append(opening.Removed ? "This post was removed."
                : body ?? E(opening.Text ?? ""))
                .Append("</div>");
            if (opening.EditedAt is not null) html.Append("<p class=\"article-edited\">Edited</p>");
            AppendSource(html, opening);
            html.Append("</article>");
        }
        html.Append("</section><section id=\"discussion\" aria-label=\"Discussion\"><h2>Replies</h2>")
            .Append("<div id=\"conversation\" class=\"conversation\" aria-label=\"Conversation\">");
        var replies = window.Posts.Where(post => post.Id != opening?.Id).ToArray();
        if (replies.Length == 0) html.Append("<p class=\"empty\">There are no replies in this window.</p>");
        foreach (var post in replies) AppendPost(html, post, post.Id == anchorId);
        html.Append("</div></section></div>");
        if (headings.Count > 0)
        {
            html.Append("<nav class=\"article-outline\" aria-label=\"Article outline\"><h2>In this article</h2><ol>");
            foreach (var (id, heading) in headings)
                html.Append("<li><a href=\"#").Append(E(id)).Append("\">").Append(E(heading)).Append("</a></li>");
            html.Append("</ol></nav>");
        }
        html.Append("</div>");
    }

    private static void AppendMobileOutline(StringBuilder html, IReadOnlyList<(string Id, string Title)> headings)
    {
        html.Append("<details class=\"article-outline-mobile\"><summary>In this article</summary><ol>");
        foreach (var (id, heading) in headings)
            html.Append("<li><a href=\"#").Append(E(id)).Append("\">").Append(E(heading)).Append("</a></li>");
        html.Append("</ol></details>");
    }

    private static int ReadingMinutes(string? text)
        => Math.Max(1, ((text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length ?? 0) + 219) / 220);

    private static string ArticleBody(string sanitizedHtml, List<(string Id, string Title)> headings)
    {
        var document = new HtmlParser().ParseDocument("<body>" + sanitizedHtml + "</body>");
        foreach (var heading in document.QuerySelectorAll("h1,h2,h3,h4,h5,h6"))
        {
            var id = "article-heading-" + (headings.Count + 1).ToString(CultureInfo.InvariantCulture);
            heading.SetAttribute("id", id);
            headings.Add((id, heading.TextContent.Trim()));
        }
        return document.Body?.InnerHtml ?? sanitizedHtml;
    }

    private static void AppendPost(StringBuilder html, PublicPostDescription post, bool anchor)
    {
        html.Append("<article class=\"post");
        if (anchor) html.Append(" post-anchor");
        html.Append("\" id=\"post-").Append(E(post.Id)).Append('"');
        if (anchor) html.Append(" aria-current=\"true\"");
        var accepted = post.AcceptedAt.ToUniversalTime();
        html.Append("><header>");
        AppendAuthorMark(html, post.Author);
        AppendAuthorName(html, post);
        html.Append(post.Author.Classification == "Agent" ? "<span class=\"agent-badge\">Agent</span>" : "").Append("<time datetime=\"")
            .Append(E(accepted.ToString("O", CultureInfo.InvariantCulture))).Append("\">")
            .Append(E(accepted.ToString("d MMM yyyy · HH:mm 'UTC'", CultureInfo.InvariantCulture)))
            .Append("</time></header><div class=\"post-text");
        if (post.Removed) html.Append(" removed");
        else if (post.Format == Conversation.PostContent.Markdown) html.Append(" markdown");
        html.Append("\">").Append(post.Removed ? "This post was removed."
            : post.Format == Conversation.PostContent.Markdown ? post.Html ?? "" : E(post.Text ?? ""))
            .Append("</div><footer class=\"post-meta\">");
        if (post.EditedAt is not null) html.Append("<span>Edited</span>");
        AppendSource(html, post);
        html.Append("<a href=\"").Append(E(post.Path)).Append("\">Permalink</a></footer></article>");
    }

    private static void AppendAuthorName(StringBuilder html, PublicPostDescription post)
    {
        var source = System.Text.RegularExpressions.Regex.Match(post.SourceUri ?? "", @"^at://(did:[^/]+)/app\.bsky\.feed\.post/([a-z0-9]+)$");
        var href = source.Success ? "https://bsky.app/profile/" + Uri.EscapeDataString(source.Groups[1].Value)
            : post.Author.ProfileUrl?.StartsWith("/u/", StringComparison.Ordinal) == true ? post.Author.ProfileUrl : null;
        html.Append("<strong>");
        if (href is not null) html.Append("<a href=\"").Append(E(href)).Append("\">");
        html.Append(E(post.Author.Label));
        if (href is not null) html.Append("</a>");
        html.Append("</strong>");
    }

    private static void AppendSource(StringBuilder html, PublicPostDescription post)
    {
        if (post.Removed || post.SourceUri is null) return;
        var source = System.Text.RegularExpressions.Regex.Match(post.SourceUri, @"^at://(did:[^/]+)/app\.bsky\.feed\.post/([a-z0-9]+)$");
        if (!source.Success) return;
        html.Append("<a class=\"source-attribution\" href=\"https://bsky.app/profile/")
            .Append(E(Uri.EscapeDataString(source.Groups[1].Value))).Append("/post/")
            .Append(E(source.Groups[2].Value)).Append("\" rel=\"noopener noreferrer\">From Bluesky ↗</a>");
    }

    private static void AppendAuthorMark(StringBuilder html, PublicParticipantDescription author)
    {
        html.Append("<span class=\"public-avatar").Append(author.Classification == "Agent" ? " is-agent" : "").Append("\" aria-hidden=\"true\">");
        if (author.Avatar is { Length: > 0 } avatar)
            html.Append("<img src=\"").Append(E(avatar)).Append("\" alt=\"\" loading=\"lazy\" decoding=\"async\">");
        else html.Append(E(string.IsNullOrEmpty(author.Label) ? "?" : StringInfo.GetNextTextElement(author.Label).ToUpperInvariant()));
        html.Append("</span>");
    }

    private static void AppendNavigation(StringBuilder html, string topicPath, PublicPostWindow window)
    {
        if (window.OlderBefore is null && window.NewerAfter is null) return;
        html.Append("<nav class=\"window-nav\" aria-label=\"Conversation pages\">");
        if (window.OlderBefore is { } older)
            html.Append("<a rel=\"prev\" href=\"").Append(E(topicPath)).Append("?before=").Append(E(Uri.EscapeDataString(older))).Append("\">← Older posts</a>");
        else html.Append("<span></span>");
        if (window.NewerAfter is { } newer)
            html.Append("<a rel=\"next\" href=\"").Append(E(topicPath)).Append("?after=").Append(E(Uri.EscapeDataString(newer))).Append("\">Newer posts →</a>");
        html.Append("</nav>");
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
