using Koan.Web.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>Every Tangent the reader may view, at <c>/t</c>, a page at a time in the Tangents' order
/// (<see cref="ViewableTangents"/>), one resource both ways (<see cref="TangentsPage"/>): where the door's list and the
/// switcher send a reader for the rest. A reader without <see cref="Bit.View"/> on the place receives its gate.</summary>
public sealed class TangentsController(KoanWebBaseUrl baseUrl, IDataProtectionProvider protection) : ControllerBase
{
    public const string Route = "/t";

    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    /// <summary>The most Tangents one page lists.</summary>
    public const int PageSize = 50;

    [HttpGet(Route)]
    [HttpHead(Route)]
    public async Task<IActionResult> Get([FromQuery(Name = After)] string? after, CancellationToken ct)
    {
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return DoorController.Gated(place.Gate(Bit.View) ?? Capabilities.Absent);

        ViewableTangents.Cursor? cursor = null;
        if (after is not null && (cursor = ViewableTangents.Cursor.Open(after, protection)) is null)
            return new Problem(StatusCodes.Status400BadRequest, "Not a page of these Tangents",
                $"The {After} parameter is the cursor a page of the Tangents names as its next.").ToResult();

        var page = await ViewableTangents.Read(personId, ViewableTangents.Views, cursor, PageSize, ct);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var baseAddress = baseUrl.Resolve(HttpContext);
        var address = baseAddress + Route;
        var back = baseUrl.PathBase + Route;
        if (personId is not null) Response.Headers.CacheControl = "private";
        return Ok(new TangentsPage(
            page.Tangents.Select(tangent => Listed(tangent, baseAddress)).ToList(),
            page.Next is { } next ? address + "?" + After + "=" + Uri.EscapeDataString(next.Seal(protection)) : null,
            cursor is not null,
            address,
            place.Can,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(back) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, back, ct),
            place.Holds(Bit.ManageSpace) ? baseAddress + NewTangentController.Route : null));
    }

    /// <summary>A Tangent as a list of them shows it: its slug, name, kind, address and the Topics started there.</summary>
    public static TangentPage.Listed Listed(Tangent tangent, string baseAddress)
        => new(tangent.Id, tangent.Name, PostWrites.Word(tangent.DefaultKind), PostWrites.TangentAddress(baseAddress, tangent.Id), tangent.TopicCount);
}
