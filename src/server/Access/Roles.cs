using Koan.Identity.Roles;

namespace Tangent.Access;

/// <summary>The roles a Tangent Space has from its first start, on Koan's <see cref="RoleCollection"/>:
/// <see cref="Everyone"/>, the base every bag holds, with no bits of its own, since a public place gives everyone
/// <see cref="Bit.View"/> through its audience and the row holds whatever else a proprietor gives everyone; a member
/// role; a moderator role; and an administrator role whose one bit implies all. They stand in Koan's one order
/// (<see cref="Role.Position"/>) from the lowest: everyone, member, moderator, administrator. Each is seeded once: a role
/// that exists stays as the proprietor set it, and one that is gone returns directly above the seeded role below it.
/// The administrator role holds <see cref="Bit.Administrator"/> at every start, which a start adds where it is missing,
/// taking nothing away. A role the proprietor creates is keyed <c>role:</c> and its word (<see cref="Word(string)"/>)
/// and stands wherever it is placed.</summary>
public static class Roles
{
    /// <summary>The base role. Its id is the token Koan puts in every bag, so the bag and the row name one thing.</summary>
    public const string Everyone = RoleTokens.Everyone;

    public const string Member = "role:member";

    public const string Moderator = "role:moderator";

    public const string Administrator = "role:administrator";

    /// <summary>What a member does in the place: read, reply, start Topics, and edit or remove their own Posts.</summary>
    public const Bit MemberBits = Bit.View | Bit.Post | Bit.Topic | Bit.EditOwn | Bit.RemoveOwn;

    /// <summary>A member who also moderates every Tangent and the standing of participants.</summary>
    public const Bit ModeratorBits = MemberBits | Bit.Moderate | Bit.ManageStanding;

    /// <summary>The seeded roles from the highest down, with the word the account menu shows for each.</summary>
    private static readonly (string Role, string Word)[] Words =
        [(Administrator, "Administrator"), (Moderator, "Moderator"), (Member, "Member")];

    /// <summary>The highest seeded role among <paramref name="roleIds"/>, in words; null for none.</summary>
    public static string? Word(IEnumerable<string> roleIds)
    {
        var held = roleIds.ToHashSet(StringComparer.Ordinal);
        return Words.FirstOrDefault(pair => held.Contains(pair.Role)).Word;
    }

    /// <summary>Whether a role is one every place has from its first start, which stays: <see cref="Everyone"/>, the
    /// member, the moderator and the administrator role.</summary>
    public static bool Seeded(string key) => key is Everyone or Member or Moderator or Administrator;

    /// <summary>Whether a role is given and taken by someone who manages it: every role but <see cref="Everyone"/> and
    /// Koan's <see cref="RoleTokens.Authenticated"/>, which every bag holds, and the member role, which admission
    /// gives.</summary>
    public static bool Given(string key) => key.StartsWith(RoleTokens.RolePrefix, StringComparison.Ordinal) && key != Member;

    /// <summary>The word a role is named by in a form and in JSON: its key after <c>role:</c>, as <c>moderator</c>; the key
    /// itself for one without that prefix.</summary>
    public static string Word(string key) => key.StartsWith(RoleTokens.RolePrefix, StringComparison.Ordinal) ? key[RoleTokens.RolePrefix.Length..] : key;

    /// <summary>A role given or taken, in words, from the role's word: "made you a moderator", "took your night shift
    /// role".</summary>
    public static string Changed(string word, bool given)
    {
        var spoken = word.Replace('-', ' ');
        return given ? "made you " + (spoken.Length > 0 && "aeiou".Contains(spoken[0]) ? "an " : "a ") + spoken : "took your " + spoken + " role";
    }

    /// <summary>The key of the role a word names (<see cref="Word(string)"/>): <c>role:</c> and the word, where the word is a
    /// slug; null otherwise.</summary>
    public static string? KeyOf(string? word) => Posts.Slug.IsValid(word) ? RoleTokens.RolePrefix + word : null;

    /// <summary>Defines each role that does not exist yet, directly above the seeded role below it; a role that exists is
    /// left as it is and where it stands, except that the administrator role is given <see cref="Bit.Administrator"/>
    /// where it lacks it, so the operator's role always administers.</summary>
    public static async Task Seed(RoleCollection roles, CancellationToken ct)
    {
        var below = await Define(roles, Everyone, "Everyone", Bit.None, null, ct);
        below = await Define(roles, Member, "Member", MemberBits, below + 1, ct);
        below = await Define(roles, Moderator, "Moderator", ModeratorBits, below + 1, ct);
        await Define(roles, Administrator, "Administrator", Bit.Administrator, below + 1, ct);
        var administrator = await roles.Get(Administrator, ct);
        var token = Bits.Token(Bit.Administrator);
        if (administrator is not null && !administrator.Permissions.Contains(token, StringComparer.Ordinal))
            await roles.SetPermissions(Administrator, [.. administrator.Permissions, token], ct);
    }

    /// <summary>The role keyed <paramref name="id"/>, defined at <paramref name="position"/> where it does not exist;
    /// where it stands.</summary>
    private static async Task<int> Define(RoleCollection roles, string id, string name, Bit bits, int? position, CancellationToken ct)
        => (await roles.Get(id, ct) ?? await roles.Define(id, name, Bits.Tokens(bits), position: position, ct: ct)).Position;
}
