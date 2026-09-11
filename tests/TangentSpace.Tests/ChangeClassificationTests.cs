using System.Reflection;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Connector.Onnx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TangentSpace.Conversation;
using Xunit;

namespace TangentSpace.Tests;

/// <summary>Change classification (D5+D6): deterministic facet diff and surface metrics, the
/// honesty rule for a missing embedder, and — only when the model artifacts actually sit beside
/// the tests — one real semantic distance through the pinned ONNX adapter.</summary>
public sealed class ChangeClassificationTests
{
    private static PostFacet Mention(int start, int end, string did)
        => new() { Kind = PostFacet.Mention, Start = start, End = end, Did = did };

    private static PostFacet Group(int start, int end, string value)
        => new() { Kind = PostFacet.Group, Start = start, End = end, Value = value };

    [Fact]
    public void Facet_diff_reports_add_remove_and_retarget()
    {
        // Retarget: the same label range re-bound to a different DID counts as remove+add.
        var retarget = ChangeClassification.DiffFacets([Mention(0, 4, "did:a")], [Mention(0, 4, "did:b")]);
        Assert.Equal(["did:b"], retarget.MentionsAdded);
        Assert.Equal(["did:a"], retarget.MentionsRemoved);
        Assert.True(retarget.GroupsAdded.Count == 0 && retarget.GroupsRemoved.Count == 0);

        var added = ChangeClassification.DiffFacets(null, [Mention(0, 4, "did:a")]);
        Assert.Equal(["did:a"], added.MentionsAdded);
        Assert.Empty(added.MentionsRemoved);

        var removed = ChangeClassification.DiffFacets([Mention(0, 4, "did:a")], null);
        Assert.Equal(["did:a"], removed.MentionsRemoved);
        Assert.Empty(removed.MentionsAdded);
    }

    [Fact]
    public void Facet_diff_counts_occurrences_and_groups_and_ignores_range_moves()
    {
        // A multiset diff: two mentions of the same DID dropping to one removes one occurrence.
        var occurrence = ChangeClassification.DiffFacets(
            [Mention(0, 4, "did:a"), Mention(5, 9, "did:a")], [Mention(0, 4, "did:a")]);
        Assert.Equal(["did:a"], occurrence.MentionsRemoved);
        Assert.Empty(occurrence.MentionsAdded);

        // The same DID at a new range is not a target change.
        var moved = ChangeClassification.DiffFacets([Mention(0, 4, "did:a")], [Mention(9, 13, "did:a")]);
        Assert.True(moved.IsEmpty);

        var groups = ChangeClassification.DiffFacets([Group(0, 7, "admins")], [Group(0, 10, "members")]);
        Assert.Equal(["members"], groups.GroupsAdded);
        Assert.Equal(["admins"], groups.GroupsRemoved);
    }

    [Fact]
    public void Surface_distance_bounds_and_known_values()
    {
        Assert.Equal(0, ChangeClassification.SurfaceDistance("kitten", "kitten"));
        Assert.Equal(0, ChangeClassification.SurfaceDistance("", ""));
        // lev("kitten","sitting") = 3 over a maximum length of 7.
        Assert.Equal(3.0 / 7, ChangeClassification.SurfaceDistance("kitten", "sitting"), 12);
        Assert.Equal(1, ChangeClassification.SurfaceDistance("abc", "xyz"));
        Assert.Equal(1, ChangeClassification.SurfaceDistance("", "abc"));
        Assert.True(ChangeClassification.SurfaceDistance("abc", "abd") is > 0 and < 1);
    }

    [Fact]
    public void Token_overlap_bounds_and_known_values()
    {
        Assert.Equal(1, ChangeClassification.TokenOverlap("the cat sat", "the cat sat"));
        Assert.Equal(1, ChangeClassification.TokenOverlap("", ""));
        Assert.Equal(0, ChangeClassification.TokenOverlap("alpha beta", "gamma delta"));
        // {alpha} vs {alpha, beta}: intersection 1, union 2.
        Assert.Equal(0.5, ChangeClassification.TokenOverlap("alpha", "alpha beta"));
        // Case and whitespace normalize; sets are distinct tokens.
        Assert.Equal(1, ChangeClassification.TokenOverlap("Alpha  beta", "alpha beta"));
    }

    [Fact]
    public void Semantic_is_null_and_marked_inactive_without_an_embedder()
    {
        var result = ChangeClassification.Classify("old text", "new text", null, null, null, CancellationToken.None).Result;
        Assert.Null(result.SemanticDistance);
        Assert.Equal("facet+levenshtein+jaccard+semantic:inactive", result.Classifier);
        Assert.Null(result.FacetDelta);
        // lev("old text","new text") = 3 over a maximum length of 8; token sets share "text" (1 of 3).
        Assert.Equal(0.375, result.SurfaceDistance, 12);
        Assert.Equal(1.0 / 3, result.TokenOverlap, 12);
    }

    [Fact]
    public void Deleted_change_classifies_old_text_against_empty()
    {
        var result = ChangeClassification.Classify("please review", "",
            [Mention(7, 21, "did:a")], null, null, CancellationToken.None).Result;
        Assert.Equal(1, result.SurfaceDistance, 12);
        Assert.Equal(0, result.TokenOverlap, 12);
        Assert.Equal(["did:a"], result.FacetDelta!.MentionsRemoved);
        Assert.Null(result.SemanticDistance);
    }

    [Fact]
    public async Task Real_model_yields_a_real_semantic_distance()
    {
        var embedder = RealEmbedder();
        if (embedder is null)
        {
            // Honest skip: the ONNX artifacts are not present beside the tests, so no real
            // semantic value can be computed. Nothing is asserted and nothing is faked.
            return;
        }

        var result = await ChangeClassification.Classify(
            "The build finished successfully.", "The build finished successfully.", null, null, embedder, CancellationToken.None);
        Assert.Equal(0, result.SemanticDistance!.Value, 4);
        Assert.DoesNotContain("inactive", result.Classifier);
        Assert.Contains("onnx:", result.Classifier);

        var rewrite = await ChangeClassification.Classify(
            "The build finished successfully.", "Lunch is soup again today.", null, null, embedder, CancellationToken.None);
        Assert.True(rewrite.SemanticDistance!.Value > 0.1);
        Assert.True(rewrite.SemanticDistance!.Value <= 2);
    }

    /// <summary>The pinned connector's adapter constructor is internal, so the test builds it
    /// through the contributor's factory with the side-loaded artifacts. Null (skip honestly)
    /// whenever the artifacts or the factory are unavailable.</summary>
    private static ChangeEmbedder? RealEmbedder()
    {
        var model = Path.Combine(AppContext.BaseDirectory, "models", "all-MiniLM-L6-v2", "model_quantized.onnx");
        var vocab = Path.Combine(AppContext.BaseDirectory, "models", "all-MiniLM-L6-v2", "vocab.txt");
        if (!File.Exists(model) || !File.Exists(vocab)) return null;
        try
        {
            var contributor = typeof(OnnxOptions).Assembly.GetType(
                "Koan.AI.Connector.Onnx.Initialization.OnnxAdapterContributor");
            var create = contributor?.GetMethod("CreateAdapter", BindingFlags.NonPublic | BindingFlags.Static);
            if (create is null) return null;
            var services = new ServiceCollection().AddOptions()
                .Configure<OnnxOptions>(options => { options.ModelPath = model; options.VocabPath = vocab; })
                .BuildServiceProvider();
            var adapter = (IEmbedAdapter)create.Invoke(null, [services])!;
            return new ChangeEmbedder(async (text, ct) =>
            {
                var response = await adapter.Embed(new AiEmbeddingsRequest { Input = [text] }, ct);
                return response.Vectors.FirstOrDefault();
            }, $"{adapter.Type}:{adapter.Name}");
        }
        catch (Exception)
        {
            return null;
        }
    }
}
