namespace Tangent.Render;

/// <summary>The form an article is written in, laid out as the article reads: its cover and title in their places, the byline,
/// then the body in the article's type, which the article island makes its visual editor; what the page puts above it, beside
/// its fields, below it and after it.</summary>
public sealed record ArticleEditor : IPart
{
    public required string Heading { get; init; }
    public required string TangentAddress { get; init; }
    public required string Tool { get; init; }
    public required string Action { get; init; }
    public required Person Author { get; init; }
    public required string Door { get; init; }
    public required string TitleId { get; init; }
    public required string BodyId { get; init; }
    public required string Island { get; init; }
    public int Minutes { get; init; } = 1;
    [Newtonsoft.Json.JsonIgnore] public required Microsoft.AspNetCore.Components.RenderFragment Actions { get; init; }
    public DateTimeOffset At { get; init; }
    public string? IslandProps { get; init; }
    public Form? Bring { get; init; }
    public string? Participant { get; init; }
    public string? EditScope { get; init; }
    public string? Topic { get; init; }
    public string? Title { get; init; }
    public string? Body { get; init; }
    public string? CoverId { get; init; }
    public string? Failure { get; init; }
    public string? FailureGate { get; init; }
    public string? TitleFailure { get; init; }
    public string? BodyFailure { get; init; }
    [Newtonsoft.Json.JsonIgnore] public Microsoft.AspNetCore.Components.RenderFragment? Hidden { get; init; }
    [Newtonsoft.Json.JsonIgnore] public Microsoft.AspNetCore.Components.RenderFragment? Above { get; init; }
    [Newtonsoft.Json.JsonIgnore] public Microsoft.AspNetCore.Components.RenderFragment? Below { get; init; }
    [Newtonsoft.Json.JsonIgnore] public Microsoft.AspNetCore.Components.RenderFragment? After { get; init; }
}

/// <summary>The way back to a Tangent from a page under it, in its rail: its name and the way to all its Topics.</summary>
public sealed record TangentBack(string Name, string Address, string Label) : IPart;
