using System.Text.Json;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Participants;

namespace Tangent.Space;

/// <summary>The descriptor and <c>llms.txt</c>: files composed from the one row, and from the participants it names for
/// a public place, each read by key, so a request reads at most three rows.</summary>
[AnswersEveryVisitor]
public sealed class DescriptorController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [HttpGet("/tangent.json")]
    [HttpHead("/tangent.json")]
    public async Task<IActionResult> Descriptor(CancellationToken ct)
    {
        var space = await Row(ct);
        Participant? proprietor = null, operatorRow = null;
        if (space.Audience == Audience.Public)
        {
            if (space.ProprietorId is { } proprietorId) proprietor = await Participant.Get(proprietorId, ct);
            if (space.OperatorId is { } operatorId)
                operatorRow = operatorId == space.ProprietorId ? proprietor : await Participant.Get(operatorId, ct);
        }

        var descriptor = Space.Descriptor.Of(space, Address, proprietor, operatorRow, baseUrl.Current is not null);
        return ServedFile.Answer(HttpContext, JsonSerializer.Serialize(descriptor, Json) + "\n", "application/json");
    }

    /// <summary>Plain text, since a browser shows it where it would offer a Markdown file for download.</summary>
    [HttpGet("/llms.txt")]
    [HttpHead("/llms.txt")]
    public async Task<IActionResult> Llms(CancellationToken ct)
        => ServedFile.Answer(HttpContext, LlmsText.Of(await Row(ct), Address, baseUrl.Current is not null), "text/plain; charset=utf-8");

    private string Address => baseUrl.Resolve(HttpContext);

    private static async Task<TangentSpace> Row(CancellationToken ct)
        => await TangentSpace.Get(TangentSpace.SingleRow, ct)
            ?? throw new InvalidOperationException("The Tangent Space row is missing.");
}
