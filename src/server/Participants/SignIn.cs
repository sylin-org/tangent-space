using Koan.Web.Auth.Infrastructure;

namespace Tangent.Participants;

/// <summary>The sign-in page as a resource, for a visitor: it says where to go, <see cref="Challenge"/>, the Koan
/// connector's atproto challenge, which comes back here once the person has signed in and sends them on to
/// <see cref="Return"/> (add <c>identifier</c> with a handle or DID to choose the account). A sign-in that fails comes
/// back here too, with Koan's sanitized code in <c>error</c> (WEB-0080). <see cref="Handle"/> is the handle typed
/// last, kept in the field after a failure; <see cref="Failure"/> is a failure of the sign-in as a whole and
/// <see cref="HandleFailure"/> one of the handle itself, both in words. <see cref="Door"/> is the place's
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
    /// <summary>The longest handle the page keeps: the longest a DNS name can be.</summary>
    public const int HandleLimit = 253;

    /// <summary>What each of Koan's sign-in failure codes says on the page: a failure of the handle shows under the
    /// field, every other one above the form. "Sign-in" is written with a non-breaking hyphen, so it never breaks across
    /// lines.</summary>
    private static readonly Dictionary<string, (string Words, bool OfHandle)> Failures = new(StringComparer.Ordinal)
    {
        [SignInFailureReturn.Code(SignInFailure.HandleUnresolved)] =
            ("No AT Protocol account answers to this handle. Check its spelling and continue.", true),
        [SignInFailureReturn.Code(SignInFailure.ProviderUnreachable)] =
            ("Your account’s provider couldn’t be reached. Continue to try again in a moment.", false),
        [SignInFailureReturn.Code(SignInFailure.AccessDenied)] =
            ("The sign‑in was declined on your provider’s page. Continue to try again.", false),
        [SignInFailureReturn.Code(SignInFailure.CallbackMismatch)] =
            ("The sign‑in came back without the request it began with. Continue to start again.", false),
        [SignInFailureReturn.Code(SignInFailure.Failed)] =
            ("Your provider didn’t confirm the sign‑in. Continue, then approve it there.", false)
    };

    /// <summary>The page for a visitor after the failure Koan's code names, in words, keeping <paramref name="handle"/>
    /// when it is one a field can hold. An unknown code reads as a sign-in that failed. Neither the code nor anything
    /// else a query carries reaches the page as it came.</summary>
    public static SignIn ForVisitor(string name, string door, string to, string challenge, string? handle, string? error)
    {
        var kept = handle is { Length: > 0 and <= HandleLimit } && handle.All(c => c is > ' ' and <= '~') ? handle : null;
        if (string.IsNullOrEmpty(error)) return new SignIn(name, door, to, challenge, kept);
        var (words, ofHandle) = Failures.TryGetValue(error, out var known) ? known : Failures[SignInFailureReturn.Code(SignInFailure.Failed)];
        return ofHandle
            ? new SignIn(name, door, to, challenge, kept, HandleFailure: words)
            : new SignIn(name, door, to, challenge, kept, Failure: words);
    }

    /// <summary>The landing a failed sign-in returns to, which the handle form completes with the typed handle.</summary>
    public string FailureLanding()
        => Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(Challenge).Query).TryGetValue(AuthConstants.Query.Failure, out var value)
            ? value.ToString()
            : "";

    /// <summary>The challenge's address without its query: where the handle form sends the handle.</summary>
    public string ChallengeAddress() => Challenge.Split('?', 2)[0];

    /// <summary>The address the challenge comes back to, as the handle form carries it.</summary>
    public string ChallengeReturn()
        => Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(Challenge).Query).TryGetValue("return", out var value)
            ? value.ToString()
            : "";
}
