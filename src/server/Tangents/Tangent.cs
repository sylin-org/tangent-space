using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;
using Tangent.Posts;

namespace Tangent.Tangents;

/// <summary>A subject area inside the Tangent Space, keyed by its slug. Its audience is the <c>everyone</c> overwrite on
/// its resource (<see cref="Overwrites"/>).</summary>
public sealed class Tangent : Entity<Tangent>
{
    public const int NameLimit = 80;

    public const int DescriptionLimit = 1000;

    /// <summary>The segment under <c>/t/</c> where a Tangent is created, which no Tangent's slug is.</summary>
    public const string NewSegment = "new";

    /// <summary>How many numbered suffixes <see cref="FreeSlug"/> tries when another Tangent holds a slug.</summary>
    public const int SuffixBound = 50;

    public static readonly Reason NeedsSlug = Reason.Invalid("A Tangent's slug is lowercase letters and digits joined by single hyphens, at most 64 characters.");
    public static readonly Reason SlugReserved = Reason.Invalid("A Tangent's slug is never new, the address where a Tangent is created.");
    public static readonly Reason NeedsName = Reason.Invalid("A Tangent has a name.");
    public static readonly Reason NameTooLong = Reason.Invalid("A Tangent's name holds at most 80 characters.");
    public static readonly Reason DescriptionTooLong = Reason.Invalid("A Tangent's description holds at most 1000 characters.");
    public static readonly Reason AccentRule = Reason.Invalid("A Tangent's accent is one of six: choose amber, coral, jade, sky, iris or rose, or send it empty for the one its slug gives it.");
    public static readonly Reason ImageRule = Reason.Invalid("A Tangent's image is one of the bundled covers, by its name, or an image brought in here, as /media/ and its id; empty takes it away.");

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>The tone its tile and its accent wear, one of <see cref="Render.Tile.Tones"/>; empty for the one its slug gives
    /// it (<see cref="Render.Tile.For(Tangent, string)"/>).</summary>
    public string Accent { get; set; } = "";

    /// <summary>The image its tile wears in place of its letter: a bundled cover's name (<see cref="Space.Art"/>) or an image
    /// brought in here, as <c>/media/{id}</c>; empty for none.</summary>
    public string Image { get; set; } = "";

    /// <summary>Takes <paramref name="accent"/> and <paramref name="image"/>, each left as it is where null, or refuses with
    /// <see cref="AccentRule"/> or <see cref="ImageRule"/>. Answers whether anything changed.</summary>
    public bool Wear(string? accent, string? image)
    {
        var tone = accent?.Trim() ?? Accent;
        if (tone.Length > 0 && !Render.Tile.Tones.Contains(tone)) throw new Refusal(AccentRule);
        var picture = image is null ? Image : Space.Art.Judged(image) ?? throw new Refusal(ImageRule);
        if (tone == Accent && picture == Image) return false;
        (Accent, Image) = (tone, picture);
        return true;
    }

    /// <summary>The kind a Topic started here has unless its author says otherwise.</summary>
    public PostKind DefaultKind { get; set; }

    /// <summary>Its place among the Tangents, lowest first.</summary>
    public int Order { get; set; }

    /// <summary>How a participant outside it comes in while it is private.</summary>
    public Joining Joining { get; set; }

    /// <summary>How many Topics it holds: raised in the commit that starts one or moves one in, lowered in the one that
    /// moves one out or deletes one (<see cref="TopicCame"/>, <see cref="TopicWent"/>).</summary>
    public int TopicCount { get; set; }

    /// <summary>How many of its Topics started in each month, by its key (<see cref="TopicList.Month.Key"/>), moved with
    /// <see cref="TopicCount"/>; a month with none has no key.</summary>
    public Dictionary<string, int> Months { get; set; } = [];

    /// <summary>How many of its Topics each participant started, by their id, moved with <see cref="TopicCount"/>; an author
    /// with none has no key.</summary>
    public Dictionary<string, int> Authors { get; set; } = [];

    /// <summary>The root Id of the Topic its moderators keep at the top of its lists; null for none. A pin that names a root
    /// removed or no longer here leads nothing (<see cref="TopicList.Pinned"/>).</summary>
    public string? PinnedId { get; set; }

    /// <summary>Pins the Topic whose root is <paramref name="topicId"/>, in place of any other. Answers whether anything
    /// changed.</summary>
    public bool Pin(string topicId)
    {
        if (PinnedId == topicId) return false;
        PinnedId = topicId;
        return true;
    }

    /// <summary>Unpins the Topic whose root is <paramref name="topicId"/>, where it is the one pinned. Answers whether
    /// anything changed.</summary>
    public bool Unpin(string topicId)
    {
        if (PinnedId != topicId) return false;
        PinnedId = null;
        return true;
    }

    /// <summary>The log's sequence of the <see cref="Events.EventKind.TangentChanged"/> that created it, written by the
    /// command that creates it; zero where none was written. Its own rows of the log begin there: a row of its slug before it
    /// belongs to a Tangent deleted before this one took the slug (<see cref="DeleteTangent"/>).</summary>
    public long LogSeq { get; set; }

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

    /// <summary>The order that puts a new Tangent after the existing ones, the moderators' Tangent aside, which comes after
    /// every other: one more than the highest below it; one bounded read of one row.</summary>
    public static async Task<int> NextOrder(CancellationToken ct)
    {
        var last = await Query(tangent => tangent.Order < Space.ModeratorsTangent.Order,
            new QueryDefinition { Page = 1, PageSize = 1 }.WithSort<Tangent>(sort => sort.OrderByDescending(tangent => tangent.Order)), ct);
        return last.Count == 0 ? 1 : last[0].Order + 1;
    }

    /// <summary>The Tangents in their order — by order, then slug — or in the reverse where <paramref name="earlier"/>,
    /// past the place <paramref name="after"/> names as a stored order and slug where it names one, the one
    /// <paramref name="aside"/> names left out; at most <paramref name="take"/>. One read, a seek on the stored values.</summary>
    public static async Task<IReadOnlyList<Tangent>> InOrder((int Order, string Id)? after, bool earlier, string? aside, int take,
        CancellationToken ct)
    {
        var query = new QueryDefinition { Filter = after is { } place ? Beyond(place, earlier) : null, Page = 1, PageSize = take }
            .WithSort<Tangent>(sort =>
            {
                if (earlier) sort.OrderByDescending(row => row.Order).ThenByDescending(row => row.Id);
                else sort.OrderBy(row => row.Order).ThenBy(row => row.Id);
            });
        var left = aside ?? "";
        return await Query(row => row.Id != left, query, ct);
    }

    /// <summary>The Tangents before or after a stored order and slug in the Tangents' order.</summary>
    private static Filter Beyond((int Order, string Id) place, bool earlier)
    {
        var order = FieldPath.Of(nameof(Order));
        var id = FieldPath.Of(nameof(Id));
        var beyond = earlier ? FilterOperator.Lt : FilterOperator.Gt;
        return Filter.Any(
            Filter.On(order, beyond, FilterValue.Of(place.Order)),
            Filter.All(Filter.On(order, FilterOperator.Eq, FilterValue.Of(place.Order)), Filter.On(id, beyond, FilterValue.Of(place.Id))));
    }

    private static void Check(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new Refusal(NeedsName);
        if (name.Trim().Length > NameLimit) throw new Refusal(NameTooLong);
        if (description.Length > DescriptionLimit) throw new Refusal(DescriptionTooLong);
    }

    /// <summary>Counts <paramref name="root"/>, a Topic started here or moved in: in all, in the month it started and by its
    /// author.</summary>
    public void TopicCame(Post root)
    {
        TopicCount = checked(TopicCount + 1);
        Tally(root, 1);
    }

    /// <summary>Stops counting <paramref name="root"/>, a Topic moved out or deleted: in all, in the month it started and by
    /// its author, never below none, a month or an author left with none dropped.</summary>
    public void TopicWent(Post root)
    {
        TopicCount = Math.Max(0, TopicCount - 1);
        Tally(root, -1);
    }

    /// <summary>Whether its months and its authors each add up to the Topics it holds.</summary>
    public bool Tallied() => Months.Values.Sum() == TopicCount && Authors.Values.Sum() == TopicCount;

    /// <summary>Counts again, in their months and by their authors, the Topics <paramref name="roots"/> names, every Topic
    /// it holds (<see cref="TallyTopics"/>).</summary>
    public void Retally(IEnumerable<Post> roots)
    {
        (Months, Authors) = ([], []);
        foreach (var root in roots) Tally(root, 1);
    }

    private void Tally(Post root, int by)
    {
        Shift(Months, TopicList.Month.Of(root.CreatedAt).Key, by);
        Shift(Authors, root.AuthorId, by);
    }

    private static void Shift(Dictionary<string, int> counts, string key, int by)
    {
        var count = checked(counts.GetValueOrDefault(key) + by);
        if (count > 0) counts[key] = count;
        else counts.Remove(key);
    }

    /// <summary>Whether this row says what the command says.</summary>
    public bool Same(string name, string description, PostKind defaultKind, int order) =>
        Name == name.Trim() && Description == description && DefaultKind == defaultKind && Order == order;
}
