using System.Text.RegularExpressions;

namespace Tangent.Access;

/// <summary>
/// The small, application-owned access contract persisted on a Tangent or Topic.
/// A null Topic decision inherits its Tangent decision; an empty decision denies
/// everyone except the matching global authority token added to the effective map.
/// </summary>
public sealed class AccessMap
{
    public string[]? See { get; set; }
    public string[]? Post { get; set; }
    public string[]? Manage { get; set; }
    public string[]? CreateTangents { get; set; }
    public string[]? CreateTopics { get; set; }

    public static AccessMap ServerDefaults() => new()
    {
        See = [AccessTokens.Everyone],
        Manage = [],
        CreateTangents = []
    };

    /// <summary>The default Tangent decision is no decision: every category falls to the table
    /// (everyone reads; the always-on permissions post, create and manage). A Space grants its
    /// members by naming roles in its access lists, never by requiring a built-in role.</summary>
    public static AccessMap TangentDefaults() => new();

    public static AccessMap TopicDefaults() => new();

    public AccessMap NormalizeForServer()
    {
        if (Post is not null || CreateTopics is not null)
            throw new ArgumentException("Post and Create Topics are not Server access decisions.");
        return new AccessMap
        {
            See = AccessCriteria.Normalize(See ?? ServerDefaults().See),
            Manage = AccessCriteria.Normalize(Manage ?? ServerDefaults().Manage),
            CreateTangents = AccessCriteria.Normalize(CreateTangents ?? ServerDefaults().CreateTangents)
        };
    }

    public AccessMap NormalizeForTangent() => new()
    {
        // A null See stays null: it is the category's empty decision, and See's empty decision is
        // everyone. Post, Manage and Create Topics normalize empty — their empty decision is the
        // category's always-on permission alone, which resolution appends.
        See = See is null ? null : AccessCriteria.Normalize(See),
        Post = AccessCriteria.Normalize(Post ?? []),
        Manage = AccessCriteria.Normalize(Manage ?? []),
        CreateTopics = AccessCriteria.Normalize(CreateTopics ?? []),
        CreateTangents = CreateTangents is null ? null
            : throw new ArgumentException("Create Tangents is a Server access decision, not a Tangent access decision.")
    };

    public AccessMap NormalizeForTopic()
    {
        if (CreateTopics is not null || CreateTangents is not null)
            throw new ArgumentException("Create Tangents and Create Topics are not Topic access decisions.");
        return new AccessMap
        {
            See = See is null ? null : AccessCriteria.Normalize(See),
            Post = Post is null ? null : AccessCriteria.Normalize(Post),
            Manage = Manage is null ? null : AccessCriteria.Normalize(Manage),
            CreateTangents = null,
            CreateTopics = null
        };
    }

    /// <summary>Resolve Topic inheritance (ADR 0013 §5): a null decision inherits its parent's,
    /// two listed decisions intersect so a child narrows and never widens, an explicit empty
    /// decision intersects to the category's always-on permission alone, and a decision absent at
    /// every level falls to the table — everyone reads, the always-on permissions do the rest.</summary>
    public AccessMap ResolveTopic(AccessMap tangent)
    {
        var local = NormalizeForTopic();
        var parent = tangent.NormalizeForTangent();
        return new AccessMap
        {
            See = Narrow(local.See, parent.See, TangentPermissions.ReadTopics, fallsTo: AccessTokens.Everyone),
            Post = Narrow(local.Post, parent.Post, TangentPermissions.CreatePosts),
            Manage = Narrow(local.Manage, parent.Manage, TangentPermissions.ManageTopics),
            CreateTangents = null,
            CreateTopics = null
        };
    }

    /// <summary>The one narrowing rule every category shares. `fallsTo` is the category table's
    /// answer when no level decides; See's answer is everyone, the write categories' is the
    /// always-on permission alone.</summary>
    private static string[] Narrow(string[]? local, string[]? parent, string alwaysOn, string? fallsTo = null)
    {
        var tokens = local is null && parent is null ? new[] { fallsTo ?? alwaysOn }
            : local is null ? parent ?? []
            : parent is null ? local
            : local.Intersect(parent, StringComparer.Ordinal).ToArray();
        return AccessCriteria.With(tokens.Length == 0 ? [alwaysOn] : tokens, alwaysOn);
    }

    public AccessMap ResolveTangent()
    {
        var selected = NormalizeForTangent();
        return new AccessMap
        {
            See = AccessCriteria.With(selected.See ?? [AccessTokens.Everyone], TangentPermissions.ReadTangents),
            Post = AccessCriteria.With(selected.Post, TangentPermissions.CreatePosts),
            Manage = AccessCriteria.With(selected.Manage, TangentPermissions.ManageTangents),
            CreateTangents = null,
            CreateTopics = AccessCriteria.With(selected.CreateTopics, TangentPermissions.CreateTopics)
        };
    }

    public AccessMap ResolveServer()
    {
        var selected = NormalizeForServer();
        return new AccessMap
        {
            See = AccessCriteria.With(selected.See, TangentPermissions.ReadHost),
            Manage = AccessCriteria.With(selected.Manage, TangentPermissions.ManageHost),
            CreateTangents = AccessCriteria.With(selected.CreateTangents, TangentPermissions.CreateTangents)
        };
    }
}

public static class AccessTokens
{
    public const string Everyone = "everyone";
    public const string Authenticated = "authenticated";
    public const string TangentMember = "tangent:member";
}

/// <summary>A pure any-of token match. Persistence, inheritance and cache work happen before this call.</summary>
public static partial class AccessCriteria
{
    public const int MaximumTokens = 64;

    public static string[] Normalize(IEnumerable<string>? tokens)
    {
        if (tokens is null) return [];
        var normalized = tokens.Select(token => token?.Trim().ToLowerInvariant() ?? "")
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length > MaximumTokens)
            throw new ArgumentException($"An access decision can contain at most {MaximumTokens} tokens.");
        var invalid = normalized.FirstOrDefault(token => token is not (AccessTokens.Everyone or AccessTokens.Authenticated or AccessTokens.TangentMember)
            && !QualifiedToken().IsMatch(token));
        if (invalid is not null)
            throw new ArgumentException($"'{invalid}' is not an access token. Use everyone, authenticated, role:<key>, or global:<permission>.");
        return normalized;
    }

    internal static string[] With(IEnumerable<string>? tokens, string token)
        => Normalize((tokens ?? []).Append(token));

    [GeneratedRegex("^(?:role|global):[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex QualifiedToken();
}

public sealed record AccessMapView(AccessMap Selected, AccessMap Effective, IReadOnlyList<string> Inherited,
    AccessMap? Parent = null);
