using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>The Tangents the reader may view, one resource both ways: a page of <see cref="Tangents"/> in the Tangents'
/// order, each with its kind and the Topics started there, <see cref="Next"/>, the address of the page after, null on the
/// last, and <see cref="First"/>, the first page's, for a page that <see cref="Continued"/> from a cursor. <see cref="Can"/>
/// is what the reader holds in the place; <see cref="NewTangent"/> is where a Tangent is created, for whoever may;
/// <see cref="SignIn"/> is where a visitor signs in, coming back here.</summary>
public sealed record TangentsPage(
    IReadOnlyList<TangentPage.Listed> Tangents,
    string? Next,
    bool Continued,
    string First,
    IReadOnlyList<string> Can,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? NewTangent);
