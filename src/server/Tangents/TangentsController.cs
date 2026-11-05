using System.ComponentModel;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
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
/// switcher send a reader for the rest, and, for a participant who takes part in the place, the members-only Tangents
/// they may join or ask to join, each with what it is about and the form that does it (<see cref="Outside"/>). A reader
/// without <see cref="Bit.View"/> on the place receives its gate.</summary>
public sealed class TangentsController(KoanWebBaseUrl baseUrl, IDataProtectionProvider protection) : ControllerBase
{
    public const string Route = "/t";

    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    /// <summary>The most Tangents one page lists.</summary>
    public const int PageSize = 50;

    [HttpGet(Route)]
    [HttpHead(Route)]
    [Tool(Name = "read_tangents")]
    [Description("List the Tangents you may read, a page at a time, and the members-only ones you may join or ask to join, which carry joining. Follow with read_tangent on one's slug, start_topic in it, or join_tangent.")]
    public async Task<IActionResult> Get([FromQuery(Name = After), Description(Holds.After)] string? after, CancellationToken ct)
    {
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);

        ViewableTangents.Cursor? cursor = null;
        if (after is not null && (cursor = ViewableTangents.Cursor.Open(after, protection)) is null)
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {After} parameter is the cursor a page of the Tangents names as its next.") { Title = "Not a page of these Tangents" }.ToResult();

        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        // A participant who takes part in the place also finds the members-only Tangents they may join or ask to join.
        var joins = personId is not null && place.Holds(Bit.Post);
        var page = await ViewableTangents.Read(personId,
            (row, on) => ViewableTangents.Views(on) || (joins && Inside.Joinable(row, space.ModeratorsTangentId)), cursor, PageSize, ct);
        var outside = page.Tangents.Where(row => !ViewableTangents.Views(page.Each[row.Id])).ToList();
        var asks = outside.Count == 0
            ? new Dictionary<string, Ask>()
            : (await Ask.Get(outside.Select(row => Ask.Key(row.Id, personId!)).ToList(), ct)).OfType<Ask>().ToDictionary(ask => ask.TangentId, StringComparer.Ordinal);
        var baseAddress = baseUrl.Resolve(HttpContext);
        var address = baseAddress + Route;
        var back = baseUrl.PathBase + Route;
        if (personId is not null) Response.Headers.CacheControl = "private";
        return Ok(new TangentsPage(
            page.Tangents.Select(tangent => ViewableTangents.Views(page.Each[tangent.Id])
                ? Listed(tangent, baseAddress)
                : Outside(tangent, baseAddress, asks.GetValueOrDefault(tangent.Id))).ToList(),
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

    /// <summary>A members-only Tangent as someone outside it finds it listed: what it is about, how they come in, and the
    /// form that joins or asks, or that withdraws their waiting ask.</summary>
    public static TangentPage.Listed Outside(Tangent tangent, string baseAddress, Ask? ask)
    {
        var address = PostWrites.TangentAddress(baseAddress, tangent.Id);
        var joining = ask is null ? tangent.Joining == Joining.Join ? "join" : "ask" : ask.Declined ? Joined.DeclinedState : Joined.AskedState;
        return Listed(tangent, baseAddress) with
        {
            Description = tangent.Description,
            Joining = joining,
            Join = ask is null ? Form.Posting(address + "/" + JoinController.JoinSegment) : null,
            Withdraw = ask is { Declined: false } ? Form.Posting(address + "/" + JoinController.LeaveSegment) : null
        };
    }

    /// <summary>A Tangent as a list of them shows it: its slug, name, kind, address and the Topics started there.</summary>
    public static TangentPage.Listed Listed(Tangent tangent, string baseAddress)
        => new(tangent.Id, tangent.Name, PostWrites.Word(tangent.DefaultKind), PostWrites.TangentAddress(baseAddress, tangent.Id), tangent.TopicCount);
}
