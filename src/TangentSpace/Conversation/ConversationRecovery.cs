using TangentSpace.AtProtocol;

namespace TangentSpace.Conversation;

internal static class ConversationRecovery
{
    internal const string ReauthorizationRequired = "reauthorization-required";

    internal static bool RequiresReauthorization(Exception error)
        => error is SpacesUnavailable failure && (failure.Stage == "session"
            // These stages use the participant's OAuth session. A rejected, freshly issued
            // Space credential on a remote read is not proof that this session needs renewal.
            || (failure.Stage is "write" or "delegate" or "create-space" or "verify-space"
                && (failure.Status == 401 || (failure.Status is 400 or 403
                    && failure.Code is "ScopeMissingError" or "insufficient_scope"
                        or "AuthenticationRequired" or "InvalidToken" or "ExpiredToken" or "invalid_token"))));

    internal static bool CanAttempt(WriteIntent intent, bool background)
        => intent.State == "pending" && (!background || intent.Detail != ReauthorizationRequired);

    internal static void RecordFailure(WriteIntent intent, Exception error)
    {
        if (error is WriteConflict)
        {
            intent.State = "conflict";
            intent.Detail = "source-key-has-different-content";
        }
        else intent.Detail = RequiresReauthorization(error) ? ReauthorizationRequired
            : "source-unavailable-retry-same-operation";
    }
}
