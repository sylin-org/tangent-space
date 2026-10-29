using Koan.Data.Core;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>Caches the profile the profile service answered on the participant it belongs to, with the time of
/// capture, and the kind its bot self-label says unless the participant chose theirs. The profile job reads the
/// network and sends this; nothing here reads anything but the row.</summary>
public sealed record CaptureProfile(ProfileAnswer Answer, DateTimeOffset At) : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(Sequences sequences, CancellationToken ct)
    {
        var participant = await Participant.Get(Answer.Did, ct);
        if (participant is null) return new Outcome<Participant>.Refused($"No participant is {Answer.Did}.");
        participant.Capture(Answer, At);
        await participant.Save(ct);
        return new Outcome<Participant>.Done(participant);
    }
}
