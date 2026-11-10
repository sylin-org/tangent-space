using Koan.Web.Controllers;

namespace Tangent.Render;

/// <summary>The place's band across the top of a panel that speaks for it: its mark and its name over its atmosphere.</summary>
public sealed record PlaceBand(string Place) : IPart
{
    /// <summary>The mark drawn for a place with no image of its own: the first letter of its name.</summary>
    public string Initial => Place.Length > 0 ? char.ToUpperInvariant(Place[0]).ToString() : "T";
}

/// <summary>The one way onward from a panel, as its main button, or, where it is the way <see cref="Back"/>, quietly under it.</summary>
public sealed record Onward(string Label, string Address) : IPart
{
    public bool Back { get; init; }
}

/// <summary>Code inside a sentence, in the mono face, an address breaking only after a slash.</summary>
public sealed record Code(string Text) : IPart;

/// <summary>A Post's earlier versions, newest first, each with who wrote it and when and its words as they stood; that only
/// the most recent are shown, where older ones stand beyond these; and the way back to the Post as it is.</summary>
public sealed record Versions(Posts.PostHistory History) : IPart;

/// <summary>A refusal as a person reads it: its heading, its sentence, what each field sent was refused for, the way to sign
/// in and come back where its word is signing in, and, for someone signed in the place answers by who they are, the account
/// they are signed in with and the way to sign out.</summary>
public sealed record Refused(Problem Problem, string? SignIn) : IPart;

/// <summary>A refusal drawn as a page: one panel in the place's frame.</summary>
[Page(Layout.Panel)]
public sealed record RefusalPage(Refused Refused)
{
    [Slot(Slot.Column)]
    public Refused Shown => Refused;
}

/// <summary>Who sends a form, beside its button: the words before the name, the name, and where the name leads, where it
/// leads anywhere.</summary>
public sealed record Signed(string Words, string Name, string? Address = null) : IPart;
