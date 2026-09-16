using System.Globalization;
using System.Net;
using System.Text;

namespace Tangent.Api;

/// <summary>Escaped, dependency-free public documents built from the shared public projection.</summary>
internal static class PublicPageRenderer
{
    internal static string Topic(PublicPostWindow window, string canonicalPath)
        => Render(window, canonicalPath, anchorId: null);

    internal static string Post(PublicPostDocument document, string canonicalPath)
        => Render(document.Window, canonicalPath, document.AnchorId);

    private static string Render(PublicPostWindow window, string canonicalPath, string? anchorId)
    {
        var topic = window.Topic;
        var title = anchorId is null ? topic.Title : $"Post in {topic.Title}";
        var description = string.IsNullOrWhiteSpace(topic.Topic) ? $"A public Topic on Tangent Space: {topic.Title}"
            : topic.Topic.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (description.Length > 180) description = description[..177] + "…";
        var html = new StringBuilder(4096);
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<meta name=\"robots\" content=\"noindex,follow\">")
            .Append("<meta name=\"description\" content=\"").Append(E(description)).Append("\">")
            .Append("<link rel=\"canonical\" href=\"").Append(E(canonicalPath)).Append("\">")
            .Append("<link rel=\"icon\" type=\"image/svg+xml\" href=\"/favicon.svg\">")
            .Append("<link rel=\"stylesheet\" href=\"/public.css?v=20260913-public1\">")
            .Append("<title>").Append(E(title)).Append(" · Tangent Space</title></head><body>")
            .Append("<a class=\"skip\" href=\"#conversation\">Skip to conversation</a>")
            .Append("<header class=\"masthead\"><a class=\"brand\" href=\"/\"><span class=\"mark\" aria-hidden=\"true\"></span>Tangent Space</a>")
            .Append("<a class=\"sign-in\" href=\"/sign-in/?return=").Append(E(Uri.EscapeDataString(canonicalPath)))
            .Append("\">Sign in to take part</a></header><main>")
            .Append("<nav class=\"breadcrumbs\" aria-label=\"Breadcrumb\"><a href=\"/\">Home</a><span aria-hidden=\"true\">/</span><span>")
            .Append(E(topic.TangentKey)).Append("</span><span aria-hidden=\"true\">/</span>");
        if (anchorId is not null)
            html.Append("<a href=\"").Append(E(topic.Path)).Append("\">").Append(E(topic.Title)).Append("</a><span aria-hidden=\"true\">/</span><span aria-current=\"page\">Post</span>");
        else html.Append("<span aria-current=\"page\">").Append(E(topic.Title)).Append("</span>");
        html.Append("</nav><header class=\"topic-hero\"><p class=\"eyebrow\">Public Topic</p><h1>")
            .Append(E(title)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(topic.Topic)) html.Append("<p class=\"topic-intro\">").Append(E(topic.Topic)).Append("</p>");
        html.Append("<p class=\"public-note\"><span aria-hidden=\"true\">●</span> Publicly readable · signed-in participation</p></header>")
            .Append("<section id=\"conversation\" class=\"conversation\" aria-label=\"Conversation\">");
        if (window.Posts.Count == 0) html.Append("<p class=\"empty\">There are no public posts in this window.</p>");
        foreach (var post in window.Posts) AppendPost(html, post, post.Id == anchorId);
        html.Append("</section>");
        AppendNavigation(html, topic.Path, window);
        html.Append("</main><footer><p>This is a public copy of a Tangent Space conversation. Access can be restricted later, but copies already made may persist.</p>")
            .Append("<a href=\"https://github.com/sylin-org/tangent-space\">Run your own Tangent</a></footer></body></html>");
        return html.ToString();
    }

    private static void AppendPost(StringBuilder html, PublicPostDescription post, bool anchor)
    {
        html.Append("<article class=\"post");
        if (anchor) html.Append(" post-anchor");
        html.Append("\" id=\"post-").Append(E(post.Id)).Append('"');
        if (anchor) html.Append(" aria-current=\"true\"");
        var accepted = post.AcceptedAt.ToUniversalTime();
        html.Append("><header><strong>").Append(E(post.Author.Label)).Append("</strong><time datetime=\"")
            .Append(E(accepted.ToString("O", CultureInfo.InvariantCulture))).Append("\">")
            .Append(E(accepted.ToString("d MMM yyyy · HH:mm 'UTC'", CultureInfo.InvariantCulture)))
            .Append("</time></header><p class=\"post-text");
        if (post.Removed) html.Append(" removed");
        html.Append("\">").Append(E(post.Removed ? "This post was removed." : post.Text ?? ""))
            .Append("</p><footer class=\"post-meta\">");
        if (post.EditedAt is not null) html.Append("<span>Edited</span>");
        html.Append("<a href=\"").Append(E(post.Path)).Append("\">Permalink</a></footer></article>");
    }

    private static void AppendNavigation(StringBuilder html, string topicPath, PublicPostWindow window)
    {
        if (window.OlderBefore is null && window.NewerAfter is null) return;
        html.Append("<nav class=\"window-nav\" aria-label=\"Conversation pages\">");
        if (window.OlderBefore is { } older)
            html.Append("<a rel=\"prev\" href=\"").Append(E(topicPath)).Append("?before=").Append(older).Append("\">← Older posts</a>");
        else html.Append("<span></span>");
        if (window.NewerAfter is { } newer)
            html.Append("<a rel=\"next\" href=\"").Append(E(topicPath)).Append("?after=").Append(newer).Append("\">Newer posts →</a>");
        html.Append("</nav>");
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
