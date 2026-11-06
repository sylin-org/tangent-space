using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;

namespace Tangent.Space;

/// <summary>The door as the reader would see it in the appearance sent, with nothing saved: for whoever may change the
/// appearance, framed by the settings page beside the form. A part left out, or one it cannot take, draws as it is saved.
/// It manages nothing, so it carries the place's own CSS as the form sends it, while the settings around it leave it
/// out.</summary>
public sealed class AppearancePreviewController : ControllerBase
{
    public const string Path = "/settings/appearance/preview";

    [HttpGet(Path)]
    [HttpPost(Path)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Needs(Bit.ManageSpace)]
    public async Task<IActionResult> Preview(ChangeAppearanceInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);

        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        HttpContext.Items[HtmlFormatter.Previewed] = space.Appearance.Previewed(input.Base, input.Palette, input.Css, input.Image, input.Mark, input.Masthead, input.Footer,
            input.Intro, input.ArticleHeader);
        if (input.Description is { Length: <= TangentSpace.DescriptionLimit } description && MarkdownHtml.Tractable(description))
            HttpContext.Items[HtmlFormatter.PreviewedDescription] = description.Trim();
        Response.Headers.XFrameOptions = "SAMEORIGIN";
        var door = ActivatorUtilities.CreateInstance<DoorController>(HttpContext.RequestServices);
        door.ControllerContext = ControllerContext;
        return await door.Get(ct);
    }
}
