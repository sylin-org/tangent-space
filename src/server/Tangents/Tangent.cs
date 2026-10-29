using Koan.Data.Core.Model;
using Tangent.Posts;
using Tangent.Writer;

namespace Tangent.Tangents;

/// <summary>A subject area inside the Tangent Space, keyed by its slug. Its audience is the <c>everyone</c>
/// <see cref="Overwrite"/> on it, not a field here.</summary>
public sealed class Tangent : Entity<Tangent>
{
    public const int NameLimit = 80;

    public const int DescriptionLimit = 1000;

    public const string NeedsSlug = "A Tangent's slug is lowercase letters and digits joined by single hyphens, at most 64 characters.";
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

    /// <summary>A Tangent, or a refusal: the slug has the slug's shape and the name and description are within their limits.</summary>
    public static Tangent Create(string slug, string name, string description, PostKind defaultKind, int order)
    {
        if (!Slug.IsValid(slug)) throw new Refusal(NeedsSlug);
        if (string.IsNullOrWhiteSpace(name)) throw new Refusal(NeedsName);
        if (name.Trim().Length > NameLimit) throw new Refusal(NameTooLong);
        if (description.Length > DescriptionLimit) throw new Refusal(DescriptionTooLong);
        return new Tangent { Id = slug, Name = name.Trim(), Description = description, DefaultKind = defaultKind, Order = order };
    }

    /// <summary>Counts a Topic started here.</summary>
    public void TopicStarted() => TopicCount = checked(TopicCount + 1);

    /// <summary>Whether this row says what the command says.</summary>
    public bool Same(string name, string description, PostKind defaultKind, int order) =>
        Name == name.Trim() && Description == description && DefaultKind == defaultKind && Order == order;
}
