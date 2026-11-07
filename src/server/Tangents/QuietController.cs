using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>What quieting a Tangent sends, as a form or one JSON object.</summary>
public sealed record QuietInput([Description(Holds.Quiet)] bool? Quiet = null);

/// <summary>Quieting a Tangent, or letting it count again, at <c>/t/{tangent}/quiet</c> (<c>quiet_tangent</c>,
/// <see cref="QuietTangent"/>): a participant who may view it says whether what is new in it counts on their door. A form is sent
/// back to the Tangent; a JSON request reads whether it is quiet.</summary>
public sealed class QuietController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The segment under a Tangent's address where a participant quiets it or lets it count again;
    /// <see cref="Slug.Reserved"/> keeps it from every Topic.</summary>
    public const string QuietSegment = "quiet";

    /// <summary>The field that says whether to quiet.</summary>
    public const string QuietField = "quiet";

    /// <summary>Whether the participant quieted the Tangent, as a JSON answer says it: its slug, its address and
    /// <c>quiet</c>.</summary>
    public sealed record Quieted(string Slug, string Address, bool Quiet);

    [HttpPost("/t/{tangent}/" + QuietSegment)]
    [Tool(Name = "quiet_tangent", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.View, Participant = true)]
    [Description("Quiet a Tangent, so what is new in it stops counting in arrive's news and on your door, while replies to you, mentions of you and the Topics you follow there still do. With quiet false, it counts again.")]
    public async Task<IActionResult> Quiet([FromRoute, Description(Holds.Tangent)] string tangent, QuietInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var row = Here.Of(HttpContext).Tangent!;
        var outcome = await new QuietTangent(Here.Of(HttpContext).PersonId!, row.Id, input.Quiet ?? true).Send(ct);
        if (outcome is not Outcome<Participant>.Done { Result: var participant }) return Answers.NotDone(HttpContext, outcome);
        var address = PostWrites.TangentAddress(baseUrl.Resolve(HttpContext), row.Id);
        if (PostWrites.WantsJson(Request)) return Ok(new Quieted(row.Id, address, participant.Quiet.Contains(row.Id)));
        Response.Headers.Location = address;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>The form that quiets the Tangent at <paramref name="address"/>, or, where it is <paramref name="quiet"/>, lets
    /// it count again.</summary>
    public static Render.Form QuietForm(string address, bool quiet)
        => Render.Form.Posting(address + "/" + QuietSegment, new Render.Form.Field(QuietField, Holds.Quiet, quiet ? "false" : "true", Render.Form.Input.Hidden))
            with { Tool = "quiet-tangent", Submit = quiet ? "Stop quieting" : "Quiet this Tangent", Quiet = true };
}
