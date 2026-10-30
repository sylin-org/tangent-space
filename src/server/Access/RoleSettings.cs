using Koan.Core.Hosting.App;
using Koan.Identity.Roles;
using Tangent.Participants;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Access;

/// <summary>What shaping the place's roles answers to, for the commands that create, change and delete a role
/// (<see cref="CreateRole"/>, <see cref="ReviseRole"/>, <see cref="DeleteRole"/>) and the page that offers them
/// (<see cref="RolesController"/>): a role's name, its permissions in plain words, and who may change which role.
/// Whoever holds <see cref="Bit.ManageRoles"/> changes only the roles they manage (<see cref="People.Manages"/>), and
/// gives or takes away only the permissions they hold (<see cref="People.Holds"/>). Everyone stays as every bag holds it;
/// the seeded roles are renamed, given other permissions and moved, never deleted; and the administrator role always
/// administers.</summary>
public static class RoleSettings
{
    /// <summary>The longest name a role takes.</summary>
    public const int NameLimit = 80;

    public const string NameRule = "Name the role in 1 to 80 characters, on one line.";

    public const string PermissionsRule = "A role’s permissions are named as its can names them: View, Post, Topic, EditOwn, RemoveOwn, Moderate, "
        + "ManageTangent, ManageRoles, ManageStanding, ManageSpace and Administrator.";

    public const string NoRole = "No role here has that word.";

    public const string NotManaged = "You change only the roles below your own highest role.";

    public const string EveryoneStays = "Everyone stays as it is: every reader holds it, and a place’s audience says what they read.";

    public const string SeededStays = "The member, moderator and administrator roles stay: rename them, change what they grant or move them instead.";

    public const string Administers = "The administrator role always administers.";

    public const string NoWord = "Every word this name gives belongs to another role; choose another name.";

    /// <summary>The word of a role whose name holds no letter or digit.</summary>
    public const string Unnamed = "role";

    /// <summary>How many numbered suffixes a new role's word tries when another role holds it.</summary>
    public const int SuffixBound = 50;

    /// <summary>What a bit lets a role's members do, in plain words.</summary>
    public static string Plain(Bit bit) => bit switch
    {
        Bit.View => "Read",
        Bit.Post => "Reply",
        Bit.Topic => "Start topics",
        Bit.EditOwn => "Edit their own posts",
        Bit.RemoveOwn => "Remove their own posts",
        Bit.Moderate => "Moderate: edit, remove, lock, pin and move anyone’s posts",
        Bit.ManageTangent => "Change Tangents’ settings and access",
        Bit.ManageRoles => "Shape roles and give them",
        Bit.ManageStanding => "Decide applications, time out and ban",
        Bit.ManageSpace => "Shape the place: its settings and its Tangents",
        Bit.Administrator => "Administer: every permission, everywhere",
        _ => bit.ToString()
    };

    /// <summary>A role's name as a role keeps it, trimmed; null where it is empty, too long or on more than one line.</summary>
    public static string? Named(string? name)
    {
        var trimmed = name?.Trim();
        return trimmed is { Length: > 0 and <= NameLimit } && !trimmed.Any(char.IsControl) ? trimmed : null;
    }

    /// <summary>The bits a text names, separated by spaces or commas, each a bit's name in any case; null where a word
    /// names no bit.</summary>
    public static Bit? Parse(string text)
    {
        var bits = Bit.None;
        foreach (var word in text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Enum.TryParse<Bit>(word, ignoreCase: true, out var bit) || bit == Bit.None || !Bits.Each.Contains(bit)) return null;
            bits |= bit;
        }

        return bits;
    }

    /// <summary>What a command that changes the place's roles judges by, read when it runs: the place's row and what the
    /// actor holds in it, or why the change is refused before any role is read: the place does not exist for them
    /// (<see cref="ChangeTangentSpace.NoPlace"/>), or the gate of <see cref="Bit.ManageRoles"/>.</summary>
    public static async Task<(TangentSpace Space, Capabilities Actor)> Actor(string actorId, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) throw new Refusal(ChangeTangentSpace.NoPlace);
        var actor = await Capabilities.Load(actorId, null, null, ct);
        if (!actor.Exists) throw new Refusal(ChangeTangentSpace.NoPlace);
        if (actor.Gate(Bit.ManageRoles) is { } gate) throw new Refusal(gate);
        return (space, actor);
    }

    /// <summary>The role a word names, which the actor manages: a refusal where no role has it
    /// (<see cref="NoRole"/>) or the actor does not manage it (<see cref="NotManaged"/>).</summary>
    public static async Task<Role> Managed(RoleCollection roles, TangentSpace space, Capabilities actor, string actorId, string key,
        CancellationToken ct)
    {
        if (await roles.Get(key, ct) is not { } role) throw new Refusal(NoRole);
        if (!People.Manages(actor, actorId, space, role)) throw new Refusal(NotManaged);
        return role;
    }

    /// <summary>Koan's roles, as the commands reach them.</summary>
    public static RoleCollection Collection() => AppHost.GetRequiredService<RoleCollection>(nameof(RoleSettings));
}
