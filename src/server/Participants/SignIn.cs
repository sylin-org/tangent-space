using Koan.Web.Auth.Infrastructure;

namespace Tangent.Participants;

/// <summary>The sign-in page as a resource, for a visitor: it says where to go, <see cref="Challenge"/>, the Koan
/// connector's atproto challenge, which starts at Bluesky, whose own account chooser picks the account, and comes back
/// here once the person has signed in, sending them on to <see cref="Return"/>; <see cref="AnotherServer"/> says how to
/// name an account on another server. A sign-in that fails comes back here too, with Koan's sanitized code in
/// <c>error</c> (WEB-0080). <see cref="Handle"/> is the handle typed last, kept in the field after a failure;
/// <see cref="Failure"/> is a failure of the sign-in as a whole and <see cref="HandleFailure"/> one of the handle
/// itself, both in words. <see cref="Door"/> is the place's door. <see cref="Private"/> says the place is read by its
/// participants alone, so signing in is how to read it.</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record SignIn(
    string Name,
    string Door,
    string Return,
    string Challenge,
    string? Handle = null,
    string? Failure = null,
    string? HandleFailure = null,
    bool Private = false)
{
    [Render.Slot(Render.Slot.Head), Newtonsoft.Json.JsonIgnore]
    public Render.PlaceBand Band => new(Name);




    /// <summary>How to sign in with an account the challenge's start at Bluesky cannot find.</summary>
    public string AnotherServer => AnotherServerText;

    /// <summary>The sentence <see cref="AnotherServer"/> answers, which first run carries too.</summary>
    public const string AnotherServerText = "For an account on a server other than Bluesky's, add identifier with its handle or DID to the challenge.";

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

    /// <summary>The challenge's address without its query: where the sign-in forms go.</summary>
    public string ChallengeAddress() => Challenge.Split('?', 2)[0];

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new(Private ? Access.Capabilities.SignInToRead.Text.TrimEnd('.') : "Sign in to reply",
        Lede: "Use your Bluesky account, or any AT\u00a0Protocol account.");

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Render.Notice.Of(null, Failure, null);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public IReadOnlyList<Render.Form> Ways => Forms(ChallengeAddress(), ChallengeReturn(), FailureLanding(), Handle, HandleFailure, "sign-in");

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.SignInGains Gains => new(Private);

    /// <summary>Signing in: with Bluesky, whose own account chooser does the rest, and behind one line, by the handle of an
    /// account on another server, open with its handle and why where a sign-in by handle came back; each comes back to
    /// <paramref name="back"/>, and a failure lands at <paramref name="failure"/>, carrying the handle typed.</summary>
    public static IReadOnlyList<Render.Form> Forms(string challenge, string back, string? failure, string? handle, string? refused, string key)
    {
        Render.Form.Field[] carried = failure is null
            ? [new("return", "", back, Render.Form.Input.Hidden)]
            : [new("return", "", back, Render.Form.Input.Hidden), new("failure", "", failure, Render.Form.Input.Hidden)];
        var byHandle = handle is not null || refused is not null;
        return
        [
            Render.Form.Reading(challenge, carried) with { Submit = "Sign in with Bluesky", Busy = "Continuing…", Island = "submitting", Main = true, Key = key },
            Render.Form.Reading(challenge,
            [
                new("identifier", "", handle) { Label = "Handle", Prefix = "@", Handle = true, Required = true, Placeholder = "you.example.com", Failure = refused, Focus = byHandle },
                new("return", "", back, Render.Form.Input.Hidden),
                .. failure is null ? [] : new[] { new Render.Form.Field("failure", "", failure, Render.Form.Input.Hidden) { Carries = ("identifier", "handle") } }
            ]) with { Submit = "Continue", Busy = "Continuing…", Island = "submitting", Opens = "Use another server", Open = byHandle, Key = key }
        ];
    }

    /// <summary>The frame the shell draws around the page: "Sign in" as its one crumb, and no search.</summary>
    public static Render.Frame Frame(SignIn signIn) => new(signIn.Name, signIn.Door, [new Render.Crumb("Sign in")], Searchable: false);

    /// <summary>The address the challenge comes back to, as the sign-in forms carry it.</summary>
    public string ChallengeReturn()
        => Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(Challenge).Query).TryGetValue("return", out var value)
            ? value.ToString()
            : "";
}
