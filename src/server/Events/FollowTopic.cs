using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;

namespace Tangent.Events;

/// <summary>A participant follows a Topic, or with <see cref="On"/> false stops: their <see cref="Follow"/> row says so, with
/// nothing new to them until someone else replies. Following needs only that the Topic exists for them, which it does wherever
/// they hold <see cref="Bit.View"/> on it, so a timed-out participant follows too; a Topic they may not see and an Id no
/// root has answer alike. Sending the choice that stands answers with the row and writes nothing. It appends nothing to the
/// log and moves no mark.</summary>
public sealed record FollowTopic(string ParticipantId, string TopicId, bool On = true) : ICommand<Follow>
{
    public async Task<Outcome<Follow>> Run(CancellationToken ct)
    {
        if (await Participant.Get(ParticipantId, ct) is null || await Post.Get(TopicId, ct) is not { ParentId: null } root
            || !(await Capabilities.Load(ParticipantId, root.TangentId, root, ct)).Exists)
            return new Outcome<Follow>.Refused(Seen.NoTopic);

        if (await Follow.Get(Follow.Key(ParticipantId, root.Id), ct) is { } existing && existing.On == On) return new Outcome<Follow>.Done(existing);
        var chosen = Follow.Of(ParticipantId, root, On);
        await chosen.Save(ct);
        return new Outcome<Follow>.Done(chosen);
    }
}
