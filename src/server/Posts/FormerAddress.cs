using Koan.Data.Core.Model;

namespace Tangent.Posts;

/// <summary>The address a Topic stood at before it moved, keyed <c>{tangent}/{slug}</c>, both slug-shaped, so a key names one
/// address: a link to it, or to a Post under it, leads to where the Topic stands now, for whoever may read it there
/// (<see cref="Access.Here.Find"/>). A Topic that takes the slug in that Tangent later is found by its slug first and has the
/// address, and a move out of it again writes the row anew; a row whose Topic is gone leads nowhere. Written by
/// <see cref="MoveTopic"/>, read by key on a miss alone.</summary>
public sealed class FormerAddress : Entity<FormerAddress>
{
    public static string Key(string tangentId, string slug) => tangentId + "/" + slug;

    /// <summary>The root of the Topic that stood there.</summary>
    public string TopicId { get; set; } = "";
}
