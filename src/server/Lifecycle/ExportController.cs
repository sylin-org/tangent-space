using System.Globalization;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;

using Tangent.Render;

namespace Tangent.Lifecycle;

/// <summary>Taking the place along, a section of its settings at <c>/settings/export</c>, for whoever holds
/// <see cref="Bit.ManageSpace"/>. Its <c>GET</c> is one resource both ways (<see cref="ExportSettingsPage"/>): where the whole
/// Tangent Space downloads, and a page of the Tangents the reader may view, in their order, each with where it downloads,
/// continued from the cursor in <c>after</c>; a reader who may not export is refused with the gate, <c>401</c> to sign in
/// and <c>403</c> otherwise: a page reads it with no Tangent, and a JSON request as its problem. <c>/settings/export.zip</c> and <c>/settings/export/{tangent}.zip</c> answer the zip
/// as <see cref="Export"/> writes it, as it is read, and never hold it; a Tangent the reader may not view answers as one
/// no Tangent has. A reader for whom the place does not exist receives what an address with nothing there receives.</summary>
[Managing]
public sealed class ExportController(KoanWebBaseUrl baseUrl, IDataProtectionProvider protection) : ControllerBase
{
    public const string Path = "/settings/export";

    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    private const string Extension = ".zip";

    /// <summary>Where the Tangent <paramref name="slug"/> downloads, under <paramref name="baseAddress"/>.</summary>
    public static string ZipOf(string baseAddress, string slug) => baseAddress + Path + "/" + Uri.EscapeDataString(slug) + Extension;

    [HttpGet(Path)]
    [Needs(Bit.View)]
    public async Task<IActionResult> Get([FromQuery(Name = After)] string? after, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = Here.Of(HttpContext).Around;
        if (place.Gate(Bit.ManageSpace) is { } refused && PostWrites.WantsJson(Request)) return this.Refused(refused);

        ViewableTangents.Cursor? cursor = null;
        if (after is not null && (cursor = ViewableTangents.Cursor.Open(after, protection)) is null)
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {After} parameter is the cursor a page of the Tangents names as its next.") { Title = "Not a page of the Tangents" }.ToResult();

        var baseAddress = baseUrl.Resolve(HttpContext);
        var address = baseAddress + Path;
        var here = baseUrl.PathBase + Path;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var gate = place.Gate(Bit.ManageSpace);
        var page = gate is null ? await ViewableTangents.Read(personId, ViewableTangents.Views, cursor, TangentsController.PageSize, ct) : null;
        var resource = new ExportSettingsPage(
            TangentSpaceSettingsController.Sections(place, baseAddress, TangentSpaceSettingsController.ExportSection),
            gate is null ? address + Extension : null,
            page?.Tangents.Select(tangent => new ExportSettingsPage.Download(tangent.Id, tangent.Name, PostWrites.Word(tangent.DefaultKind), tangent.TopicCount,
                ZipOf(baseAddress, tangent.Id))).ToList() ?? [],
            page?.Next is { } next ? address + "?" + After + "=" + Uri.EscapeDataString(next.Seal(protection)) : null,
            cursor is not null,
            address,
            place.Can,
            gate?.Word,
            personId is null ? baseAddress + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct));
        return gate is null ? Ok(resource) : new ObjectResult(resource) { StatusCode = Answers.Status(HttpContext, gate) };
    }

    [HttpGet(Path + Extension)]
    public Task<IActionResult> Whole(CancellationToken ct) => Download(null, ct);

    [HttpGet(Path + "/{tangent}" + Extension)]
    public Task<IActionResult> OfTangent(string tangent, CancellationToken ct) => Download(tangent, ct);

    /// <summary>The zip of the Tangent named by <paramref name="slug"/>, or of the whole place, to a reader who holds
    /// <see cref="Bit.ManageSpace"/>. Its headers leave with its first file; from there a failure ends the connection.</summary>
    private async Task<IActionResult> Download(string? slug, CancellationToken ct)
    {
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);
        if (place.Gate(Bit.ManageSpace) is { } gate) return Answers.Refused(HttpContext, gate);

        Tangents.Tangent? only = null;
        if (slug is not null)
        {
            // The Tangent's row is read before its overwrites, so an address no Tangent has takes no place in Koan's cache.
            if (!Slug.IsValid(slug) || await Tangents.Tangent.Get(slug, ct) is not { } row) return this.Refused(Capabilities.Absent);
            if (!(await Capabilities.Load(personId, row.Id, null, ct)).Exists) return this.Refused(Capabilities.Absent);
            only = row;
        }

        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var name = new ContentDispositionHeaderValue("attachment");
        name.SetHttpFileName(string.Join('-', new[] { Slug.From(space.Name, "tangent-space"), only?.Id, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }.OfType<string>()) + Extension);
        Response.Headers.ContentDisposition = name.ToString();
        Response.Headers.CacheControl = "no-store";
        Response.ContentType = "application/zip";
        // .NET's zip ends each file with a short synchronous write, however it was written.
        HttpContext.Features.GetRequiredFeature<IHttpBodyControlFeature>().AllowSynchronousIO = true;
        await Export.Write(Response.Body, personId, only, space, baseUrl.Resolve(HttpContext), HttpContext.RequestServices, ct);
        return new EmptyResult();
    }
}
