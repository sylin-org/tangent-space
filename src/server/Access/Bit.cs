using Koan.Identity.Roles;

namespace Tangent.Access;

/// <summary>What a role or an overwrite holds. On a Koan role a bit is the permission token <c>global:</c> followed by
/// its name, which Koan compiles into the member's bag; in an overwrite's allow or deny it is the name alone.
/// <see cref="Administrator"/> implies all.</summary>
[Flags]
public enum Bit
{
    None = 0,
    View = 1,
    Post = 1 << 1,
    Topic = 1 << 2,
    EditOwn = 1 << 3,
    RemoveOwn = 1 << 4,

    /// <summary>Edit or remove any Post, lock, pin, move, take on a tangent anywhere.</summary>
    Moderate = 1 << 5,
    ManageTangent = 1 << 6,
    ManageRoles = 1 << 7,
    ManageStanding = 1 << 8,
    ManageSpace = 1 << 9,
    Administrator = 1 << 10
}

/// <summary>The bits as names and as Koan's tokens.</summary>
public static class Bits
{
    public const Bit All = Bit.View | Bit.Post | Bit.Topic | Bit.EditOwn | Bit.RemoveOwn | Bit.Moderate
        | Bit.ManageTangent | Bit.ManageRoles | Bit.ManageStanding | Bit.ManageSpace | Bit.Administrator;

    /// <summary>Every bit, one at a time, in the order §7 names them.</summary>
    public static readonly IReadOnlyList<Bit> Each =
    [
        Bit.View, Bit.Post, Bit.Topic, Bit.EditOwn, Bit.RemoveOwn, Bit.Moderate,
        Bit.ManageTangent, Bit.ManageRoles, Bit.ManageStanding, Bit.ManageSpace, Bit.Administrator
    ];

    private static readonly Dictionary<string, Bit> ByName = Each.ToDictionary(bit => bit.ToString(), StringComparer.Ordinal);

    /// <summary>The permission token a Koan role holds for this one bit.</summary>
    public static string Token(Bit bit) => RoleTokens.GlobalPrefix + bit;

    /// <summary>The permission tokens a Koan role holds for these bits.</summary>
    public static IReadOnlyList<string> Tokens(Bit bits) => Names(bits).Select(name => RoleTokens.GlobalPrefix + name).ToList();

    /// <summary>The names of these bits, in order.</summary>
    public static IReadOnlyList<string> Names(Bit bits) => Each.Where(bit => bits.HasFlag(bit)).Select(bit => bit.ToString()).ToList();

    /// <summary>The bits these names spell; a name that is not a bit counts for nothing.</summary>
    public static Bit Named(IEnumerable<string> names)
    {
        var bits = Bit.None;
        foreach (var name in names)
        {
            if (ByName.TryGetValue(name, out var bit)) bits |= bit;
        }

        return bits;
    }

    /// <summary>The bits among a bag's tokens: each <c>global:</c> token that names one; every other token counts for
    /// nothing.</summary>
    public static Bit InTokens(IEnumerable<string> tokens) =>
        Named(tokens.Where(token => token.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal))
            .Select(token => token[RoleTokens.GlobalPrefix.Length..]));

    /// <summary>Whether a bag token is a role's id: not <c>everyone</c>, not <c>authenticated</c>, not a permission.</summary>
    public static bool IsRoleId(string token) =>
        token != RoleTokens.Everyone && token != RoleTokens.Authenticated
        && !token.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal);
}
