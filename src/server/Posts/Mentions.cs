using System.Text.RegularExpressions;
using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>A mention is a link in a body whose target is <c>at://</c> and a DID — <c>[@ada](at://did:plc:…)</c> — so
/// the identity is written into the body and a handle that changes later changes nothing there. At render it resolves
/// to the participant with that DID: the link goes to their address and reads their current handle; a DID that is no
/// participant's leaves the link's words as plain text. A body mentions at most <see cref="Limit"/> identities. The
/// command that writes a Post appends one <see cref="EventKind.Mention"/> per participant its body newly mentions
/// (<see cref="Notify"/>).</summary>
public static partial class Mentions
{
    /// <summary>What a mention's target starts with: atproto's URI scheme, whose authority is the DID.</summary>
    public const string Scheme = "at://";

    /// <summary>The most identities one body mentions.</summary>
    public const int Limit = 32;

    /// <summary>The most characters a mentioned DID holds.</summary>
    public const int IdentityLimit = 576;

    /// <summary>What one resolved mention may weigh in an answer: its DID, handle and address, with room to spare.</summary>
    public const int Weight = 2048;

    /// <summary>The DID a link target names when it is a mention; null for any other target.</summary>
    public static string? Identity(string target)
    {
        if (!target.StartsWith(Scheme, StringComparison.Ordinal)) return null;
        var did = target[Scheme.Length..];
        return did.Length <= IdentityLimit && Did().IsMatch(did) ? did : null;
    }

    /// <summary>The identities <paramref name="body"/> mentions, each once, in the order of their first mention.</summary>
    public static IReadOnlyList<string> In(string body)
        => MarkdownHtml.Links(body).Select(Identity).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The participants these bodies mention, by DID, as a page shows them, in one keyed read: each body holds
    /// at most <see cref="Limit"/> identities, so the read is bounded by the page's Posts.</summary>
    public static async Task<IReadOnlyDictionary<string, TopicPage.Mention>> Resolve(IEnumerable<string> bodies, string configured,
        CancellationToken ct)
    {
        var ids = bodies.SelectMany(In).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, TopicPage.Mention>();
        return (await Participant.Get(ids, ct)).OfType<Participant>().ToDictionary(participant => participant.Id, participant =>
        {
            var handle = participant.Handle.Length > 0 ? participant.Handle : participant.Id;
            return new TopicPage.Mention(participant.Id, handle, configured + "/p/" + Uri.EscapeDataString(handle));
        }, StringComparer.Ordinal);
    }

    /// <summary>The participants among <paramref name="resolved"/> that <paramref name="body"/> mentions, in the order of
    /// their first mention.</summary>
    public static IReadOnlyList<TopicPage.Mention> Of(string body, IReadOnlyDictionary<string, TopicPage.Mention> resolved)
        => In(body).Select(id => resolved.GetValueOrDefault(id)).OfType<TopicPage.Mention>().ToList();

    /// <summary>A body as safe HTML, each mention of one of <paramref name="mentioned"/> a link to their address that
    /// reads <c>@</c> and their current handle, and every other mention its own words as plain text.</summary>
    public static string Html(string body, IReadOnlyList<TopicPage.Mention> mentioned)
        => MarkdownHtml.Render(body, target => Identity(target) is not { } did
            ? null
            : mentioned.FirstOrDefault(mention => mention.Id == did) is { } mention
                ? new MarkdownHtml.Link(mention.Address, "@" + mention.Handle)
                : new MarkdownHtml.Link(null, null));

    /// <summary>Appends, inside the running command, one <see cref="EventKind.Mention"/> per participant
    /// <paramref name="post"/>'s body mentions that <paramref name="before"/> — the body before an edit, null for a new
    /// Post — did not, targeted at them: not the actor, not a DID that is no participant's, and not a participant for
    /// whom the Post's Tangent is absent. One keyed read of the participants, then capability's rows for each.</summary>
    public static async Task Notify(Sequences sequences, Post post, string actorId, string? before, DateTimeOffset at, CancellationToken ct)
    {
        var earlier = before is null ? [] : In(before).ToHashSet(StringComparer.Ordinal);
        var added = In(post.Body).Where(id => id != actorId && !earlier.Contains(id)).ToList();
        if (added.Count == 0) return;
        var participants = (await Participant.Get(added, ct)).OfType<Participant>().Select(participant => participant.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in added.Where(participants.Contains))
        {
            if (!(await Capabilities.Load(id, post.TangentId, null, ct)).Exists) continue;
            await Event.Next(sequences, EventKind.Mention, at,
                tangentId: post.TangentId, topicId: post.TopicId, postId: post.Id, actorId: actorId, targetId: id, version: post.Version).Save(ct);
        }
    }

    [GeneratedRegex(@"^did:[a-z]+:[A-Za-z0-9._:%-]+$")]
    private static partial Regex Did();
}
