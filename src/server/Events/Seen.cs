using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;

namespace Tangent.Events;

/// <summary>A participant says what they have seen, and their position moves there: with no Topic, their place mark
/// moves to <see cref="Through"/>, a sequence of the log the door read through, so nothing at or before it is new to them
/// anywhere; with a Topic, their mark on it moves to <see cref="Through"/>, a sequence of that Topic its page showed.
/// Never backwards: a sequence at or behind the mark answers with the mark as it stands and writes nothing. The
/// participant exists and the place, or the Topic, exists for them, or the command answers as for a missing one; the
/// sequence names an event of the log, or one of the Topic's Posts from its root's zero to its last, or the command is
/// refused. Seeing decides nothing: an Offer stays open and an application waits until someone answers it.</summary>
public sealed record Seen(string ParticipantId, long Through, string? TopicId = null) : ICommand<Checkpoint>
{
    public static readonly Reason NoPlace = Reason.Absent("No Tangent Space is here.");
    public static readonly Reason NoTopic = Reason.Absent("No topic is here.");
    public static readonly Reason NoEvent = Reason.Invalid("No event has this sequence.");
    public static readonly Reason NoSequence = Reason.Invalid("No post of this topic has this sequence.");

    public async Task<Outcome<Checkpoint>> Run(CancellationToken ct)
    {
        var participant = await Participant.Get(ParticipantId, ct);
        Scope scope;
        if (TopicId is null)
        {
            if (participant is null || !(await Capabilities.Load(ParticipantId, null, null, ct)).Exists) return new Outcome<Checkpoint>.Refused(NoPlace);
            if (Through < 1 || await Event.Get(Event.Key(Through), ct) is null) return new Outcome<Checkpoint>.Refused(NoEvent);
            scope = Scope.Place;
        }
        else
        {
            if (participant is null || await Post.Get(TopicId, ct) is not { ParentId: null } root
                || !(await Capabilities.Load(ParticipantId, root.TangentId, root, ct)).Exists)
                return new Outcome<Checkpoint>.Refused(NoTopic);
            if (Through < 0 || Through > root.LastSequence) return new Outcome<Checkpoint>.Refused(NoSequence);
            scope = Scope.Topic(root.Id);
        }

        var existing = await Checkpoint.Get(Checkpoint.Key(ParticipantId, scope), ct);
        if (existing is not null && existing.Seq >= Through) return new Outcome<Checkpoint>.Done(existing);

        var moved = Checkpoint.At(ParticipantId, scope, Through);
        await moved.Save(ct);
        return new Outcome<Checkpoint>.Done(moved);
    }

    /// <summary>Moves <paramref name="authorId"/>'s mark on the Topic of <paramref name="root"/> onto the Post they just
    /// wrote at <paramref name="sequence"/>, inside the command that wrote it, where their mark stood on the Post just
    /// before it — the root for the Topic they started — so their own write never reads as new to them and never passes
    /// over a Post they had not read.</summary>
    public static async Task Wrote(string authorId, Post root, long sequence, CancellationToken ct)
    {
        var key = Checkpoint.Key(authorId, Scope.Topic(root.Id));
        if (sequence > 0 && (await Checkpoint.Get(key, ct))?.Seq != sequence - 1) return;
        await Checkpoint.At(authorId, Scope.Topic(root.Id), sequence).Save(ct);
    }
}
