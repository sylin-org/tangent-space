using Microsoft.AspNetCore.Mvc;
using Tangent.Space;

namespace Tangent.Connections;

/// <summary>Where the place says which <c>site.standard.publication</c> record it is: the record's <c>at://</c> address as
/// plain text, while the place is public and its proprietor's record stands, so a reader of the network can tell the
/// record and the place are the same site. Nothing answers there otherwise.</summary>
[AnswersEveryVisitor]
public sealed class PublicationController : ControllerBase
{
    public const string Path = "/.well-known/site.standard.publication";

    [HttpGet(Path)]
    public async Task<IActionResult> Get(CancellationToken ct)
        => await TangentSpace.Get(TangentSpace.SingleRow, ct) is { Audience: Audience.Public } space
           && await Publication.Get(Publication.SingleRow, ct) is { Uri.Length: > 0 } publication && publication.Proprietor == space.ProprietorId
            ? Content(publication.Uri, "text/plain")
            : NotFound();
}
