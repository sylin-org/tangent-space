using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Participants;

namespace Tangent.Tangents;

/// <summary>A participant quiets a Tangent, or with <see cref="On"/> false lets it count again: what is new in it stops
/// counting on their door — its news, its count in the rail, the new marks on its Topics in the front page — while what is
/// for them there still reaches them, replies, mentions and the Topics they follow. The Tangent exists for them, which it
/// does wherever they hold <see cref="Bit.View"/> on it, or the command answers that no Tangent has the slug; sending the
/// choice that stands writes nothing. It appends nothing to the log.</summary>
public sealed record QuietTangent(string ParticipantId, string Slug, bool On = true) : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(CancellationToken ct)
    {
        if (await Participant.Get(ParticipantId, ct) is not { } participant || !(await Capabilities.Load(ParticipantId, Slug, null, ct)).Exists)
            return new Outcome<Participant>.Refused(ChangeTangent.NoTangent);
        if (participant.Quieten(Slug, On)) await participant.Save(ct);
        return new Outcome<Participant>.Done(participant);
    }
}
