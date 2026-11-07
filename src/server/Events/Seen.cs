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
/// refused. What it leaves new to them in the Topics they follow goes with it (<see cref="Follow.Read"/>, <see cref="Follow.Caught"/>).
/// Seeing decides nothing: an Offer stays open and an application waits until someone answers it.</summary>
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
            await Follow.Caught(ParticipantId, Through, ct);
        }
        else
        {
            if (participant is null || await Post.Get(TopicId, ct) is not { ParentId: null } root
                || !(await Capabilities.Load(ParticipantId, root.TangentId, root, ct)).Exists)
                return new Outcome<Checkpoint>.Refused(NoTopic);
            if (Through < 0 || Through > root.LastSequence) return new Outcome<Checkpoint>.Refused(NoSequence);
            scope = Scope.Topic(root.Id);
            await Follow.Read(ParticipantId, root, Through, ct);
        }

        var existing = await Checkpoint.Get(Checkpoint.Key(ParticipantId, scope), ct);
        if (existing is not null && existing.Seq >= Through) return new Outcome<Checkpoint>.Done(existing);

        var moved = Checkpoint.At(ParticipantId, scope, Through);
        await moved.Save(ct);
        return new Outcome<Checkpoint>.Done(moved);
    }

    /// <summary>Moves <paramref name="authorId"/>'s mark on the Topic of <paramref name="root"/> onto the Post they just
    /// wrote at <paramref name="sequence"/>, inside the command that wrote it: where they hold no mark there, since
    /// writing in a Topic presumes it was read, and where their mark stood on the Post just before it, so their own write
    /// never reads as new to them. A mark that stands further back stays, and never passes over a Post they had not
    /// read. Answers whether the mark now stands on the Post they wrote.</summary>
    public static async Task<bool> Wrote(string authorId, Post root, long sequence, CancellationToken ct)
    {
        var mark = await Checkpoint.Get(Checkpoint.Key(authorId, Scope.Topic(root.Id)), ct);
        if (mark is not null && mark.Seq != sequence - 1) return false;
        await Checkpoint.At(authorId, Scope.Topic(root.Id), sequence).Save(ct);
        return true;
    }
}
