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

    public static AccessMap TangentDefaults() => new()
    {
        See = ["role:member"],
        Post = ["role:member"],
        Manage = [],
        CreateTopics = ["role:member"]
    };

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
        See = AccessCriteria.Normalize(See ?? TangentDefaults().See),
        Post = AccessCriteria.Normalize(Post ?? TangentDefaults().Post),
        Manage = AccessCriteria.Normalize(Manage ?? TangentDefaults().Manage),
        CreateTopics = AccessCriteria.Normalize(CreateTopics ?? TangentDefaults().CreateTopics),
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

    /// <summary>Resolve Topic inheritance before adding its global authority grants.</summary>
    public AccessMap ResolveTopic(AccessMap tangent)
    {
        var local = NormalizeForTopic();
        var parent = tangent.NormalizeForTangent();
        return new AccessMap
        {
            See = AccessCriteria.With(local.See ?? parent.See, TangentPermissions.ReadTopics),
            Post = AccessCriteria.With(local.Post ?? parent.Post, TangentPermissions.CreatePosts),
            Manage = AccessCriteria.With(local.Manage ?? parent.Manage, TangentPermissions.ManageTopics),
            CreateTangents = null,
            CreateTopics = null
        };
    }

    public AccessMap ResolveTangent()
    {
        var selected = NormalizeForTangent();
        return new AccessMap
        {
            See = AccessCriteria.With(selected.See, TangentPermissions.ReadTangents),
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
        var invalid = normalized.FirstOrDefault(token => token is not (AccessTokens.Everyone or AccessTokens.Authenticated)
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
