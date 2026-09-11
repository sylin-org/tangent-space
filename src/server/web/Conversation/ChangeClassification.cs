using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts.Routing;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;

namespace TangentSpace.Conversation;

/// <summary>The edit-time change classification (D5+D6), computed once and stored on the
/// changelog snapshot. Advisory only — content stays authoritative. Reads are pure lookups.</summary>
/// <param name="SurfaceDistance">Normalized Levenshtein distance between old and new text, [0,1].</param>
/// <param name="TokenOverlap">Jaccard overlap of the old and new token sets, [0,1].</param>
/// <param name="FacetDelta">Exact structural diff of the mention and group facets. The trusted signal.</param>
/// <param name="SemanticDistance">Cosine distance between old/new embeddings, [0,2]; null when
/// the embedder is inactive or failed — never a faked number.</param>
/// <param name="Classifier">Identity+version of the computing pipeline, e.g.
/// <c>facet+levenshtein+jaccard+onnx:all-MiniLM-L6-v2</c>; the semantic term reads
/// <c>semantic:inactive</c> when no embedder was configured.</param>
public sealed record ChangeClass(double SurfaceDistance, double TokenOverlap, FacetDelta? FacetDelta,
    double? SemanticDistance, string Classifier);

/// <summary>Structural diff of mention facets old→new (D5): DID occurrences added and removed,
/// plus role-group names added and removed. A label range re-bound to a different DID
/// ("retargeted") falls out as remove+add because the diff is over DID occurrences, not ranges;
/// the same DID moved to a new range is not a target change.</summary>
public sealed record FacetDelta(IReadOnlyList<string> MentionsAdded, IReadOnlyList<string> MentionsRemoved,
    IReadOnlyList<string> GroupsAdded, IReadOnlyList<string> GroupsRemoved)
{
    public bool IsEmpty => MentionsAdded.Count == 0 && MentionsRemoved.Count == 0
        && GroupsAdded.Count == 0 && GroupsRemoved.Count == 0;
}

/// <summary>An embedder the classifier may use: a vector function plus the model identity for
/// the Classifier string. Null model-identity parts degrade honestly.</summary>
public sealed record ChangeEmbedder(Func<string, CancellationToken, Task<float[]?>> Vector, string Model);

/// <summary>Deterministic surface metrics, the facet diff, and the semantic term via Koan's
/// in-process ONNX connector. Every axis is computed at edit time; nothing here runs at read
/// time (digest rule). The semantic path resolves the ambient host's embed adapter and reports
/// inactive rather than inventing a distance.</summary>
public static class ChangeClassification
{
    public const string SemanticInactive = "semantic:inactive";
    public const string SemanticUnavailable = "semantic:unavailable";

    /// <summary>The first embed-capable adapter on the ambient host, or null when none is
    /// registered (unset <c>Koan:Ai:Onnx:ModelPath</c> = adapter never activates).</summary>
    public static ChangeEmbedder? ResolveEmbedder()
    {
        var registry = AppHost.Current?.GetService(typeof(IAiAdapterRegistry)) as IAiAdapterRegistry;
        var adapter = registry?.All.OfType<IEmbedAdapter>().FirstOrDefault();
        if (adapter is null) return null;
        var model = adapter.ListModels(CancellationToken.None).GetAwaiter().GetResult()
            .FirstOrDefault()?.Name ?? adapter.Name;
        return new ChangeEmbedder(async (text, ct) =>
        {
            var response = await adapter.Embed(new AiEmbeddingsRequest { Input = [text] }, ct);
            return response.Vectors.FirstOrDefault();
        }, $"{adapter.Type}:{model}");
    }

    /// <summary>Classify one change. Deleted/moderated rows pass the empty string as the new text;
    /// the new facets are null, so surface and facet axes stay meaningful and the semantic axis
    /// follows embedder availability.</summary>
    public static async Task<ChangeClass> Classify(string oldText, string newText,
        IReadOnlyList<PostFacet>? oldFacets, IReadOnlyList<PostFacet>? newFacets,
        ChangeEmbedder? embedder, CancellationToken ct)
    {
        var delta = DiffFacets(oldFacets, newFacets);
        var surface = SurfaceDistance(oldText, newText);
        var overlap = TokenOverlap(oldText, newText);
        double? semantic = null;
        var semanticTerm = embedder is null ? SemanticInactive : SemanticUnavailable;
        if (embedder is not null)
        {
            try
            {
                var oldVector = await embedder.Vector(oldText, ct);
                var newVector = await embedder.Vector(newText, ct);
                semantic = SemanticDistance(oldVector, newVector);
                if (semantic is not null) semanticTerm = embedder.Model;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // An active embedder that fails degrades to a null distance; never fake a number.
            }
        }
        return new ChangeClass(surface, overlap, delta.IsEmpty ? null : delta, semantic,
            $"facet+levenshtein+jaccard+{semanticTerm}");
    }

    /// <summary>Exact multiset diff of mention DIDs and group names, each occurrence counted.
    /// Tag and topic facets are picker-minted structure outside this axis.</summary>
    public static FacetDelta DiffFacets(IReadOnlyList<PostFacet>? oldFacets, IReadOnlyList<PostFacet>? newFacets)
    {
        var oldMentions = Occurrences(oldFacets, PostFacet.Mention, facet => facet.Did);
        var newMentions = Occurrences(newFacets, PostFacet.Mention, facet => facet.Did);
        var oldGroups = Occurrences(oldFacets, PostFacet.Group, facet => facet.Value);
        var newGroups = Occurrences(newFacets, PostFacet.Group, facet => facet.Value);
        return new FacetDelta(Added(newMentions, oldMentions), Added(oldMentions, newMentions),
            Added(newGroups, oldGroups), Added(oldGroups, newGroups));
    }

    /// <summary>Normalized Levenshtein distance over UTF-16 code units with the classic
    /// two-row dynamic program: <c>lev(a,b) / max(|a|,|b|)</c>, so 0 = identical and 1 = a
    /// complete rewrite against a non-empty baseline; both empty = 0.</summary>
    public static double SurfaceDistance(string oldText, string newText)
    {
        if (oldText.Length == 0 && newText.Length == 0) return 0;
        var span = Math.Max(oldText.Length, newText.Length);
        var previous = new int[newText.Length + 1];
        var current = new int[newText.Length + 1];
        for (var j = 0; j <= newText.Length; j++) previous[j] = j;
        for (var i = 1; i <= oldText.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= newText.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (oldText[i - 1] == newText[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return (double)previous[newText.Length] / span;
    }

    /// <summary>Token-set Jaccard overlap: distinct whitespace-delimited tokens, lowercased
    /// invariant; <c>|A ∩ B| / |A ∪ B|</c>. Both empty = 1 (two empty token sets are identical).</summary>
    public static double TokenOverlap(string oldText, string newText)
    {
        var oldTokens = Tokens(oldText);
        var newTokens = Tokens(newText);
        if (oldTokens.Count == 0 && newTokens.Count == 0) return 1;
        var intersection = 0;
        foreach (var token in oldTokens.Count <= newTokens.Count ? oldTokens : newTokens)
            if (oldTokens.Contains(token) && newTokens.Contains(token)) intersection++;
        var union = oldTokens.Count + newTokens.Count - intersection;
        return union == 0 ? 1 : (double)intersection / union;
    }

    /// <summary>Cosine distance between two L2-normalized vectors (the ONNX adapter normalizes
    /// by default, so cosine similarity is the plain dot product): <c>1 − a·b</c>, clamped to
    /// [0,2]. Null when either vector is null, empty, or dimension-mismatched.</summary>
    public static double? SemanticDistance(float[]? oldVector, float[]? newVector)
    {
        if (oldVector is not { Length: > 0 } || newVector is not { Length: > 0 }
            || oldVector.Length != newVector.Length) return null;
        double dot = 0;
        for (var i = 0; i < oldVector.Length; i++) dot += (double)oldVector[i] * newVector[i];
        return Math.Clamp(1 - dot, 0, 2);
    }

    private static Dictionary<string, int> Occurrences(IReadOnlyList<PostFacet>? facets, string kind,
        Func<PostFacet, string?> identity)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var facet in facets ?? [])
        {
            if (facet.Kind != kind || identity(facet) is not { Length: > 0 } value) continue;
            counts.TryGetValue(value, out var count);
            counts[value] = count + 1;
        }
        return counts;
    }

    private static List<string> Added(Dictionary<string, int> mine, Dictionary<string, int> theirs)
    {
        var added = new List<string>();
        foreach (var (value, count) in mine)
            for (var extra = count - theirs.GetValueOrDefault(value); extra > 0; extra--) added.Add(value);
        added.Sort(StringComparer.Ordinal);
        return added;
    }

    private static HashSet<string> Tokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            tokens.Add(token.ToLowerInvariant());
        return tokens;
    }
}
