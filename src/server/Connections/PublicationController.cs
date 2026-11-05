using Microsoft.AspNetCore.Mvc;
using Tangent.Space;

namespace Tangent.Connections;

/// <summary>Where the place says which <c>site.standard.publication</c> record it is: the record's <c>at://</c> address as
/// plain text, once its proprietor published it, so a reader of the network can tell the record and the place are the same
/// site. Nothing answers there before that.</summary>
[AnswersEveryVisitor]
public sealed class PublicationController : ControllerBase
{
    public const string Path = "/.well-known/site.standard.publication";

    [HttpGet(Path)]
    public async Task<IActionResult> Get(CancellationToken ct)
        => await Publication.Get(Publication.SingleRow, ct) is { Uri.Length: > 0 } publication
            ? Content(publication.Uri, "text/plain")
            : NotFound();
}
