using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Identity;

namespace Tangent.Api;

/// <summary>Shared card artwork storage; existing governance decides who may upload.</summary>
[ApiController]
public sealed class ArtworkController(TangentServer hub, IWebHostEnvironment environment) : ControllerBase
{
    private const int MaxBytes = 5 * 1024 * 1024;
    private string DirectoryPath => Path.Combine(environment.ContentRootPath, "artwork");

    [HttpPost("/api/artwork"), TopicMutation, RequestSizeLimit(7 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromBody] ArtworkUpload upload, CancellationToken ct)
    {
        try
        {
            var actor = ParticipationAccess.Require(User, ParticipationGrants.Welcome);
            var server = await hub.Space.Read(actor, ct);
            var allowed = upload.Scope switch
            {
                "server" => server.CanManage,
                "tangent" when !string.IsNullOrWhiteSpace(upload.TangentKey) =>
                    (await hub.Tangents.Describe(actor, upload.TangentKey, ct))?.CanManage == true,
                "tangent" => server.Permissions?.AllowedActions.Contains("createTangent") == true,
                _ => false
            };
            if (!allowed) return StatusCode(403, new { reason = "You cannot change artwork here." });
            var bytes = upload.Content;
            if (bytes is null || bytes.Length is 0 or > MaxBytes)
                return BadRequest(new { reason = "Choose an image no larger than 5 MB." });
            var extension = ImageExtension(bytes);
            if (extension is null) return BadRequest(new { reason = "Choose a PNG, JPEG, or WebP image." });
            var filename = Convert.ToHexStringLower(SHA256.HashData(bytes)) + extension;
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, filename);
            // Write atomically; an interrupted upload never becomes visible artwork.
            if (!System.IO.File.Exists(path))
            {
                var temporary = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    await System.IO.File.WriteAllBytesAsync(temporary, bytes, ct);
                    System.IO.File.Move(temporary, path, overwrite: true);
                }
                finally { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); }
            }
            return Ok(new { url = "/artwork/" + filename });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [AllowAnonymous, HttpGet("/artwork/{filename}")]
    public IActionResult Read(string filename)
    {
        var extension = Path.GetExtension(filename);
        var hash = Path.GetFileNameWithoutExtension(filename);
        if (filename != hash + extension || hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            extension is not (".png" or ".jpg" or ".webp")) return NotFound();
        var path = Path.Combine(DirectoryPath, filename);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(path, extension == ".jpg" ? "image/jpeg" : "image/" + extension[1..]);
    }

    private static string? ImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return ".png";
        if (bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return ".jpg";
        if (bytes.Length >= 16 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }
}

public sealed record ArtworkUpload(string Scope, string? TangentKey, byte[]? Content);
