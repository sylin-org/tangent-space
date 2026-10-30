namespace Tangent.Space;

/// <summary>The place's colour, one of the design's four: links, buttons and what is new take it on every page, for every
/// reader, in their own theme.</summary>
public enum Accent
{
    Gold,
    Jade,
    Iris,
    Copper
}

/// <summary>How the place looks: its <see cref="Accent"/>, which whoever holds <c>ManageSpace</c> sets and every page
/// carries as <c>data-accent</c>. Light or dark is each reader's own, kept by their browser.</summary>
public sealed class Appearance
{
    /// <summary>The accents a form offers, in their order.</summary>
    public static readonly IReadOnlyList<string> Accents = [.. Enum.GetValues<Accent>().Select(Word)];

    private static readonly object RequestAccent = new();

    public Accent Accent { get; set; } = Accent.Gold;

    /// <summary>The word an accent is written as, in a form, in JSON and on a page.</summary>
    public static string Word(Accent accent) => accent.ToString().ToLowerInvariant();

    /// <summary>The place's accent as this request's page carries it: gold before first run establishes the place. One
    /// bounded read of the row by its key per request, however many ask.</summary>
    public static async ValueTask<string> OfRequest(HttpContext http)
    {
        if (http.Items.TryGetValue(RequestAccent, out var held)) return (string)held!;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, http.RequestAborted);
        var word = Word(space?.Appearance.Accent ?? Accent.Gold);
        http.Items[RequestAccent] = word;
        return word;
    }
}
