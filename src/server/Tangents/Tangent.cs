using Koan.Data.Core.Model;
using Tangent.Posts;
using Tangent.Writer;

namespace Tangent.Tangents;

/// <summary>A subject area inside the Tangent Space, keyed by its slug. Its audience is the <c>everyone</c> overwrite on
/// its resource (<see cref="Overwrites"/>); <see cref="LoggedAudience"/> is only what the log last said of it.</summary>
public sealed class Tangent : Entity<Tangent>
{
    public const int NameLimit = 80;

    public const int DescriptionLimit = 1000;

    /// <summary>The segment under <c>/t/</c> where a Tangent is created, which no Tangent's slug is.</summary>
    public const string NewSegment = "new";

    /// <summary>How many numbered suffixes <see cref="FreeSlug"/> tries when another Tangent holds a slug.</summary>
    public const int SuffixBound = 50;

    public const string NeedsSlug = "A Tangent's slug is lowercase letters and digits joined by single hyphens, at most 64 characters.";
    public const string SlugReserved = "A Tangent's slug is never new, the address where a Tangent is created.";
    public const string NeedsName = "A Tangent has a name.";
    public const string NameTooLong = "A Tangent's name holds at most 80 characters.";
    public const string DescriptionTooLong = "A Tangent's description holds at most 1000 characters.";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>The kind a Topic started here has unless its author says otherwise.</summary>
    public PostKind DefaultKind { get; set; }

    /// <summary>Its place among the Tangents, lowest first.</summary>
    public int Order { get; set; }

    /// <summary>How many Topics were started here, raised in the commit that starts one.</summary>
    public int TopicCount { get; set; }

    /// <summary>The audience the log last recorded for this Tangent in a <c>tangent.changed</c>; null where none is
    /// known. The audience itself is the <c>everyone</c> overwrite, which Koan commits outside the writer: a change of
    /// the Tangent that finds the two apart records the overwrite's here and in the log (<see cref="ChangeTangent"/>).</summary>
    public Space.Audience? LoggedAudience { get; set; }

    /// <summary>A Tangent, or a refusal: the slug has the slug's shape and is not <see cref="NewSegment"/>, and the name
    /// and description are within their limits.</summary>
    public static Tangent Create(string slug, string name, string description, PostKind defaultKind, int order)
    {
        if (!Slug.IsValid(slug)) throw new Refusal(NeedsSlug);
        if (slug == NewSegment) throw new Refusal(SlugReserved);
        Check(name, description);
        return new Tangent { Id = slug, Name = name.Trim(), Description = description, DefaultKind = defaultKind, Order = order };
    }

    /// <summary>Renames, describes and sets the default kind, or refuses as <see cref="Create"/> does; the slug stays.
    /// Answers whether anything changed.</summary>
    public bool Change(string name, string description, PostKind defaultKind)
    {
        Check(name, description);
        if (Name == name.Trim() && Description == description && DefaultKind == defaultKind) return false;
        (Name, Description, DefaultKind) = (name.Trim(), description, defaultKind);
        return true;
    }

    /// <summary><paramref name="slug"/>, or the first numbered suffix of it that no Tangent holds and that is not
    /// <see cref="NewSegment"/>, each one keyed read; null past <see cref="SuffixBound"/>.</summary>
    public static async Task<string?> FreeSlug(string slug, CancellationToken ct)
    {
        for (var suffix = 1; suffix <= SuffixBound; suffix++)
        {
            var candidate = suffix == 1 ? slug : Slug.WithSuffix(slug, suffix);
            if (candidate != NewSegment && await Get(candidate, ct) is null) return candidate;
        }

        return null;
    }

    private static void Check(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new Refusal(NeedsName);
        if (name.Trim().Length > NameLimit) throw new Refusal(NameTooLong);
        if (description.Length > DescriptionLimit) throw new Refusal(DescriptionTooLong);
    }

    /// <summary>Counts a Topic started here.</summary>
    public void TopicStarted() => TopicCount = checked(TopicCount + 1);

    /// <summary>Whether this row says what the command says.</summary>
    public bool Same(string name, string description, PostKind defaultKind, int order) =>
        Name == name.Trim() && Description == description && DefaultKind == defaultKind && Order == order;
}
