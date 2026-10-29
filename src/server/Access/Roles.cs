using Koan.Identity.Roles;
using Tangent.Space;

namespace Tangent.Access;

/// <summary>The roles a Tangent Space has from its first start, on Koan's <see cref="RoleCollection"/>:
/// <see cref="Everyone"/>, the base every bag holds, with <see cref="Bit.View"/> on a public place and nothing on a
/// private one; a member role; a moderator role; and an administrator role whose one bit implies all. Each is seeded
/// once: a role that exists stays as the proprietor set it.</summary>
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

    /// <summary>Defines each role that does not exist yet; a role that exists is left as it is.</summary>
    public static async Task Seed(RoleCollection roles, Audience audience, CancellationToken ct)
    {
        await Define(roles, Everyone, "Everyone", audience == Audience.Public ? Bit.View : Bit.None, ct);
        await Define(roles, Member, "Member", MemberBits, ct);
        await Define(roles, Moderator, "Moderator", ModeratorBits, ct);
        await Define(roles, Administrator, "Administrator", Bit.Administrator, ct);
    }

    private static async Task Define(RoleCollection roles, string id, string name, Bit bits, CancellationToken ct)
    {
        if (await roles.Get(id, ct) is null) await roles.Define(id, name, Bits.Tokens(bits), ct: ct);
    }
}
