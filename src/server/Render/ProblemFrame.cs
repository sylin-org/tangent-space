using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace Tangent.Render;

/// <summary>What Koan refuses before a route answers, as a person reads it: a value that cannot bind, a form past its limit,
/// a gate. It becomes the place's own <see cref="Problem"/>, so it reads as the place's refusals do, in its frame.</summary>
public sealed class ProblemFrame(KoanWebBaseUrl baseUrl) : IProblemView
{
    public IActionResult Render(HttpContext http, ProblemDetails problem) => new Problem(
        problem.Status ?? StatusCodes.Status400BadRequest,
        problem.Title ?? "Not accepted",
        problem.Detail ?? Fields(problem),
        Word(problem, "gate"),
        Word(problem, "next") is { } next ? baseUrl.Resolve(http) + next : null).ToResult();

    /// <summary>What each field a request sent could not be, in the words Koan names it by.</summary>
    private static string? Fields(ProblemDetails problem) => problem is ValidationProblemDetails { Errors.Count: > 0 } invalid
        ? string.Join(" ", invalid.Errors.Select(field => (field.Key.Length > 0 ? field.Key + ": " : "") + string.Join(" ", field.Value)))
        : null;

    private static string? Word(ProblemDetails problem, string name) => problem.Extensions.TryGetValue(name, out var word) ? word as string : null;
}
