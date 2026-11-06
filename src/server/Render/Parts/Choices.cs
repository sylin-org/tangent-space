namespace Tangent.Render;

/// <summary>A role's permissions as a form chooses them: each permission, the ones chosen marked, its ids set apart by
/// <see cref="Prefix"/>.</summary>
public sealed record RolePermissions(string Prefix, IReadOnlyList<Access.RolesPage.Permission> Permissions, IReadOnlyList<string> Chosen) : IPart;

/// <summary>What a Tangent's form chooses beside its name: its description, its kind and who reads it, each with what it
/// means; read only for a reader who may not change them, the audience fixed where the place decides it.</summary>
public sealed record TangentChoices(string Prefix, string Description, string Kind, string Audience, IReadOnlyList<string> Kinds,
    IReadOnlyList<string> Audiences, string? DescriptionFailure = null, bool ReadOnly = false, bool Quiet = false, string? AudienceFixed = null) : IPart;
