using AngleSharp.Html.Parser;
using Tangent.Api;
using Xunit;

namespace Tangent.Tests;

public sealed class PublicWorkspaceTests
{
    [Fact]
    public void Public_document_keeps_content_and_canonical_link_while_using_current_workspace_assets()
    {
        const string original = "<!doctype html><html><head><link rel=\"canonical\" href=\"/t/place/topics/story?before=post-9\"></head><body><h1>A public story</h1><p>Readable without JavaScript.</p><a href=\"?before=post-8\">Older posts</a></body></html>";
        const string workspace = "<html><head><script type=\"module\" src=\"/assets/index-a1.js\"></script><link rel=\"stylesheet\" href=\"/assets/index-b2.css\"><script type=\"module\" src=\"https://external.example/code.js\"></script></head></html>";
        var document = new HtmlParser().ParseDocument(PublicPageRenderer.WithWorkspace(original, workspace));
        Assert.Equal("/t/place/topics/story?before=post-9", document.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Contains("Readable without JavaScript.", document.QuerySelector("#public-document")!.TextContent);
        Assert.NotNull(document.QuerySelector("#public-document a[href='?before=post-8']"));
        Assert.NotNull(document.QuerySelector("#app[hidden][aria-hidden=true]"));
        Assert.NotNull(document.QuerySelector("script[type=module][src='/assets/index-a1.js']"));
        Assert.NotNull(document.QuerySelector("link[data-workspace-style][media='not all'][href='/assets/index-b2.css']"));
        Assert.Single(document.QuerySelectorAll("script"));
        var unavailable = new HtmlParser().ParseDocument(PublicPageRenderer.WithWorkspace(original, null));
        Assert.Contains("Readable without JavaScript.", unavailable.Body!.TextContent);
        Assert.Empty(unavailable.QuerySelectorAll("script"));
    }
}
