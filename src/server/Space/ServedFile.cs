using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Tangent.Space;

/// <summary>A file the server composes from a row and serves as it is: the body, its media type, an <c>ETag</c> that is a
/// hash of the body — so it holds while the body does and changes with it — and a cache lifetime of
/// <see cref="MaxAge"/>. A request that already holds the tag is a <c>304</c>.</summary>
public static class ServedFile
{
    /// <summary>How long a proxy or client may reuse the file before asking again.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public static IActionResult Answer(HttpContext http, string body, string mediaType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var tag = new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(SHA256.HashData(bytes).AsSpan(0, 12))}\"");
        var typed = http.Response.GetTypedHeaders();
        typed.ETag = tag;
        typed.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = MaxAge };
        if (http.Request.GetTypedHeaders().IfNoneMatch.Any(held => held.Equals(EntityTagHeaderValue.Any) || held.Compare(tag, useStrongComparison: false)))
            return new StatusCodeResult(StatusCodes.Status304NotModified);
        return new FileContentResult(bytes, mediaType);
    }
}
