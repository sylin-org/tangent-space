using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>The form that creates a Tangent, one resource both ways. It asks for a name; everything else stands at its
/// default until the sender names another: an empty <see cref="Description"/>, the <see cref="Kind"/> <c>topic</c>
/// (discussions) and the <see cref="Audience"/> <c>public</c>, where everyone who reads the place reads it and members
/// post. <see cref="Action"/> takes <c>name</c>, <c>description</c>, <c>kind</c> (one of <see cref="Kinds"/>) and
/// <c>audience</c> (one of <see cref="Audiences"/>); the slug derives from the name and never changes, and the Tangent
/// comes after the existing ones. <see cref="Token"/> is the form's antiforgery token. <see cref="Can"/> is what the
/// reader holds in the place, and <see cref="Gate"/> why they may not create a Tangent, null when they may;
/// <see cref="SignIn"/> is where a visitor signs in, coming back here. After a refusal the form keeps what was sent and
/// says why: under the name, under the description, or above the form.</summary>
public sealed record NewTangentPage(
    string Action,
    FormToken Token,
    IReadOnlyList<string> Can,
    string? Gate,
    string Name,
    string Description,
    string Kind,
    string Audience,
    IReadOnlyList<string> Kinds,
    IReadOnlyList<string> Audiences,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null,
    string? NameFailure = null,
    string? DescriptionFailure = null);
