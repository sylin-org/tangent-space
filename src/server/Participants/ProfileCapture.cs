using Koan.Jobs;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>The profile job's work: skip a profile captured within its lifetime; otherwise read the public profile
/// once and hand it to the writer. A read that fails leaves the cache as it was, and the next sign-in after its
/// lifetime submits the job again.</summary>
public static class ProfileCapture
{
    /// <summary>Submits the profile job for a participant whose cache is empty or older than its lifetime. A
    /// submission that fails is logged, never thrown: what it follows has already happened, and the next sign-in
    /// submits again.</summary>
    public static async Task SubmitWhenStale(Participant participant, ILogger logger, CancellationToken ct)
    {
        if (participant.ProfileFresh(DateTimeOffset.UtcNow)) return;
        try
        {
            await participant.Job.Submit(ct: ct);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(failure, "The profile job of {Participant} was not submitted", participant.Id);
        }
    }

    public static async Task Run(Participant participant, JobContext context, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (participant.ProfileFresh(now))
        {
            context.Logger.LogDebug("Profile of {Participant} was captured at {CapturedAt}; not read again",
                participant.Id, participant.Profile.CapturedAt);
            return;
        }

        ProfileAnswer answer;
        try
        {
            answer = await context.Services.GetRequiredService<ProfileReader>().Read(participant.Id, ct);
        }
        catch (ProfileReadFailed failed)
        {
            context.Logger.LogWarning("Profile of {Participant} not captured: {Reason}", participant.Id, failed.Message);
            return;
        }

        var outcome = await context.Services.GetRequiredService<WriteLoop>().Send(new CaptureProfile(answer, now), ct);
        switch (outcome)
        {
            case Outcome<Participant>.Done:
                return;
            case Outcome<Participant>.Refused refused:
                context.Logger.LogWarning("Profile of {Participant} not cached: {Reason}", participant.Id, refused.Reason);
                return;
            case Outcome<Participant>.Failed failed:
                throw new InvalidOperationException($"The writer could not cache the profile of {participant.Id}.", failed.Cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for the profile of {participant.Id}.");
        }
    }
}
