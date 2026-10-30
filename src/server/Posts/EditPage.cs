using Tangent.Render;

namespace Tangent.Posts;

/// <summary>The form that edits a Post, with its inline removal, one resource both ways. It names the Post, its
/// address, its Tangent and its Topic; <see cref="Edit"/> takes <c>body</c>, <c>title</c> on a root, and <c>version</c>,
/// the <see cref="Version"/> the editor read, and <see cref="Remove"/> removes the Post, each present only where the
/// reader may do it. <see cref="Can"/> is what the reader holds on the Post; <see cref="Gate"/> is why they may not edit
/// it and <see cref="RemoveGate"/> why they may not remove it, each null when they may. A tombstone is <see cref="Removed"/> and holds no body. After a refusal the form keeps what was
/// sent and says why; after an edit made from another version it holds the current text and version, and the words
/// that were not saved in <see cref="SentTitle"/> and <see cref="SentBody"/>. <see cref="BodyOptional"/> marks a Topic's
/// root taken on a tangent, which may keep no body of its author's own.</summary>
public sealed record EditPage(
    string Id,
    string Address,
    string Tangent,
    string TangentName,
    string TangentAddress,
    string TopicTitle,
    string TopicAddress,
    bool Root,
    string? Title,
    string Body,
    long Version,
    bool Removed,
    Form? Edit,
    Form? Remove,
    IReadOnlyList<string> Can,
    string? Gate,
    string? RemoveGate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null,
    string? FailureGate = null,
    string? TitleFailure = null,
    string? BodyFailure = null,
    string? SentTitle = null,
    string? SentBody = null,
    bool BodyOptional = false);
