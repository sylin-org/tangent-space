namespace Tangent.Participants;

/// <summary>The sign-in page as a resource, for a visitor: it says where to go, <see cref="Challenge"/>, the Koan
/// connector's atproto challenge, which comes back here once the person has signed in and sends them on to
/// <see cref="Return"/> (add <c>identifier</c> with a handle or DID to choose the account). <see cref="Handle"/> is
/// the handle typed last, kept in the field after a failure; <see cref="Failure"/> is a failure of the sign-in as a
/// whole and <see cref="HandleFailure"/> one of the handle itself, both in words. <see cref="Door"/> is the place's
/// door.</summary>
public sealed record SignIn(
    string Name,
    string Door,
    string Return,
    string Challenge,
    string? Handle = null,
    string? Failure = null,
    string? HandleFailure = null)
{
    /// <summary>The failure code the connector's callback answers when it could not verify a sign-in.</summary>
    public const string VerificationFailed = "atproto_authentication_failed";

    /// <summary>The failure code for a handle that resolves to no atproto account.</summary>
    public const string HandleNotFound = "handle_not_found";

    /// <summary>The longest handle the page keeps: the longest a DNS name can be.</summary>
    public const int HandleLimit = 253;

    /// <summary>The page for a visitor after <paramref name="failure"/>, in words, keeping <paramref name="handle"/>
    /// when it is one a field can hold. Neither the code nor anything else a query carries reaches the page as it
    /// came.</summary>
    public static SignIn ForVisitor(string name, string door, string to, string challenge, string? handle, string? failure)
    {
        var kept = handle is { Length: > 0 and <= HandleLimit } && handle.All(c => c is > ' ' and <= '~') ? handle : null;
        return failure switch
        {
            null or "" => new SignIn(name, door, to, challenge, kept),
            HandleNotFound => new SignIn(name, door, to, challenge, kept,
                HandleFailure: "No AT Protocol account answers to this handle. Check its spelling and continue."),
            VerificationFailed => new SignIn(name, door, to, challenge, kept,
                Failure: "Your provider didn’t confirm the sign-in. Continue, then approve it on your provider’s page."),
            _ => new SignIn(name, door, to, challenge, kept,
                Failure: "Sign-in could not be completed. Check your handle and continue to try again.")
        };
    }

    /// <summary>The challenge's address without its query: where the handle form sends the handle.</summary>
    public string ChallengeAddress() => Challenge.Split('?', 2)[0];

    /// <summary>The address the challenge comes back to, as the handle form carries it.</summary>
    public string ChallengeReturn()
        => Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(Challenge).Query).TryGetValue("return", out var value)
            ? value.ToString()
            : "";
}
