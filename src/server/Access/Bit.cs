using Koan.Identity.Roles;

namespace Tangent.Access;

/// <summary>What a role or an overwrite holds. On a Koan role and in a Koan overwrite's allow or deny, a bit is the
/// permission token <c>global:</c> followed by its name, except <see cref="Administrator"/>, which is Koan's
/// <see cref="RoleTokens.Administrator"/>: a role alone grants it, and it implies all.</summary>
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

    private static readonly Dictionary<string, Bit> ByToken = Each.ToDictionary(Token, StringComparer.Ordinal);

    /// <summary>The permission token a Koan role or overwrite holds for this one bit.</summary>
    public static string Token(Bit bit) => bit == Bit.Administrator ? RoleTokens.Administrator : RoleTokens.GlobalPrefix + bit;

    /// <summary>The permission tokens a Koan role or overwrite holds for these bits.</summary>
    public static IReadOnlyList<string> Tokens(Bit bits) => Each.Where(bit => bits.HasFlag(bit)).Select(Token).ToList();

    /// <summary>The names of these bits, in order.</summary>
    public static IReadOnlyList<string> Names(Bit bits) => Each.Where(bit => bits.HasFlag(bit)).Select(bit => bit.ToString()).ToList();

    /// <summary>The bits among permission tokens: each token that is a bit's; every other token counts for nothing.</summary>
    public static Bit InTokens(IEnumerable<string> tokens)
    {
        var bits = Bit.None;
        foreach (var token in tokens)
        {
            if (ByToken.TryGetValue(token, out var bit)) bits |= bit;
        }

        return bits;
    }
}
