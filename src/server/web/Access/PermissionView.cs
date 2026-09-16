namespace Tangent.Access;

public sealed record PermissionView(string Role, string Scope, IReadOnlyList<string> AllowedActions,
    IReadOnlyDictionary<string, string> Restrictions);
