namespace Tangent.Participants;

/// <summary>The sign-in page as a resource. For a visitor it says where to go: <see cref="Challenge"/>, the Koan
/// connector's atproto challenge, which comes back here once the person has signed in (add <c>identifier</c> with a
/// handle or DID to choose the account). For a person who signed in and has not arrived yet it carries
/// <see cref="Arrival"/>: where to declare their kind. <see cref="Failure"/> is the connector's sanitized failure,
/// in words; <see cref="Door"/> is the place's door and <see cref="Return"/> is where the participant goes once they have
/// arrived.</summary>
public sealed record SignIn(string Name, string Door, string Return, string Challenge, string? Failure = null, Arrival? Arrival = null)
{
    /// <summary>The connector's sanitized failure code, as its callback answers it.</summary>
    public const string VerificationFailed = "atproto_authentication_failed";

    /// <summary>The sanitized failure, in words, for a failure code; never the code or anything else a query carries.</summary>
    public static string? Describe(string? failure) => failure switch
    {
        null or "" => null,
        VerificationFailed => "Your sign-in could not be verified. Start again.",
        _ => "Sign-in could not be completed. Check your handle and start again."
    };

    /// <summary>The challenge's address without its query: where the handle form sends the handle.</summary>
    public string ChallengeAddress() => Challenge.Split('?', 2)[0];

    /// <summary>The address the challenge comes back to, as the handle form carries it.</summary>
    public string ChallengeReturn()
        => Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(Challenge).Query).TryGetValue("return", out var value)
            ? value.ToString()
            : "";
}

/// <summary>A signed-in person's first arrival: <see cref="Action"/> takes a form with <c>kind</c> (one of
/// <see cref="Kinds"/>) and <c>return</c>, and the place's <see cref="Admission"/> says whether they join or apply.</summary>
public sealed record Arrival(string Action, string Handle, string Admission, IReadOnlyList<string> Kinds);
