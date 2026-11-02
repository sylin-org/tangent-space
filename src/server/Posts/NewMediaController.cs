using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;

namespace Tangent.Posts;

/// <summary>What bringing an image in sends as one JSON object; a multipart form carries the image as a file of the same
/// name.</summary>
public sealed record BringInput([Description(Holds.BroughtImage)] string? File = null);

/// <summary>Where an image comes into a Tangent for someone writing there: <c>POST /t/{tangent}/media</c>, for whoever
/// may start a Topic or reply there. It takes <c>file</c>, the image as a file of a multipart form or its bytes in
/// base64 in one JSON object, keeps it (<see cref="Media.Keep"/>) and answers with its Id, its address, and the reference
/// a body names it by (<see cref="WrittenMedia"/>). The address resolves as every write's does, before anything is judged
/// of the request (<see cref="PostWrites.Locate"/>).</summary>
public sealed class NewMediaController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The segment under a Tangent's address where an image comes in.</summary>
    public const string Segment = "media";

    /// <summary>The field that holds the image.</summary>
    public const string FileField = "file";

    /// <summary>The most bytes the request holds: an image at its limit, in base64.</summary>
    public const int RequestLimit = Media.SentLimit / 3 * 4 + 64 * 1024;

    public const string NoFile = "Send the image as the file field: a file of a multipart form, or its bytes in base64 in one JSON object.";

    [HttpPost("/t/{tangent}/" + Segment)]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit, ValueLengthLimit = RequestLimit)]
    [Tool(Name = "bring_in")]
    [Description("Bring an image into a Tangent, for a Post to show or an article to lead with.")]
    public async Task<IActionResult> Bring([FromRoute, Description(Holds.Tangent)] string tangent, BringInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, null, null, ct);
        if (found is null) return unseen!;
        var here = found.Capabilities;
        if (personId is null || (!here.Holds(Bit.Topic) && !here.Holds(Bit.Post))) return PostWrites.Refused(HttpContext, here.Gate(Bit.Topic) ?? Capabilities.SignIn);

        if (await Sent(input, ct) is not { } sent) return NotKept(NoFile);
        Media media;
        try
        {
            media = await Media.Keep(sent, ct);
        }
        catch (Refusal refusal)
        {
            return NotKept(refusal.Message);
        }

        return this.Answer(await new BringMedia(media.Id, personId).Send(), _ =>
        {
            var reference = Media.Reference(media.Id);
            var address = baseUrl.Resolve(HttpContext) + reference;
            Response.Headers.Location = address;
            return new ObjectResult(new WrittenMedia(media.Id, address, reference)) { StatusCode = StatusCodes.Status201Created, ContentTypes = { "application/json" } };
        });
    }

    private async Task<byte[]?> Sent(BringInput input, CancellationToken ct)
    {
        if (Request.HasFormContentType)
        {
            if ((await Request.ReadFormAsync(ct)).Files.GetFile(FileField) is not { } file) return null;
            if (file.Length > Media.SentLimit) return new byte[Media.SentLimit + 1];
            using var bytes = new MemoryStream();
            await file.CopyToAsync(bytes, ct);
            return bytes.ToArray();
        }

        if (input.File is not { Length: > 0 } text) return null;
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IActionResult NotKept(string reason) => new Problem(StatusCodes.Status400BadRequest, "Not kept", reason).ToResult();
}

/// <summary>An image as its coming in answers: its Id, which a cover names; its address; and the reference a body's
/// Markdown names it by, as <c>![what it shows](reference)</c>.</summary>
public sealed record WrittenMedia(string Id, string Address, string Reference);
