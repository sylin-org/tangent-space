using Koan.Web.Auth.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Tangent.Render;

namespace Tangent.Participants;

/// <summary>Use another Bluesky account: a form in the page, never a dialog, that posts to Koan's binding of an atproto
/// login (SEC-0040) with the handle typed as its <c>identifier</c>, coming back to the page it was sent from as the same
/// participant, bound to the new account; their earlier Posts keep the account they were written as. Koan binds only from
/// a same-site POST after a recent sign-in, and refuses with a word on the Connections page (<see cref="FailurePath"/>),
/// which says why: <c>sign_in_first</c> with the one step that answers it, signing in again as the account they hold,
/// which comes back to this form, open, with the handle they typed; <c>login_taken</c> for an account that already
/// arrived here as someone else, which stays theirs.</summary>
public static class AnotherAccount
{
    /// <summary>Koan's binding of an atproto login to the person signed in.</summary>
    public static readonly string Route = AuthConstants.Routes.ConnectionsLink.Replace("{provider}", "atproto", StringComparison.Ordinal);

    /// <summary>The query that opens the form on the Connections page, and the query the typed handle comes back in.</summary>
    public const string OpenQuery = "another";
    public const string HandleQuery = "handle";

    /// <summary>Where a refused binding lands: the Connections page with the form open, which Koan places under the path
    /// base and gives the refusal's word in <c>error</c>.</summary>
    public static readonly string FailurePath = Connections.ConnectionsController.Path + "?" + OpenQuery + "=1";

    /// <summary>The form as the page draws it: where it posts, the page it comes back to, where a refusal lands, the
    /// handle the participant holds now, the handle to keep in the field, why the last attempt was refused, the step that
    /// answers that refusal, and whether it opens drawn open.</summary>
    public sealed record Offer(string Action, string Return, string Failure, string Holding, string? Handle = null, string? Refused = null,
        Form? SignInAgain = null, bool Open = false) : Render.IPart
    {
        /// <summary>The one line that says what stays: earlier Posts keep linking to the account they were written as.</summary>
        public string Note => "Your earlier Posts stay linked to @" + Holding + ".";

        /// <summary>The step that answers a sign-in that is not fresh enough, with why above its button, its ids under
        /// <paramref name="key"/>.</summary>
        public Form? Again(string key) => SignInAgain is not { } again ? null : again.AsGiven() with
        {
            Tool = "another-account-again", Key = key + "-again", Island = "submitting", Main = true, Submit = "Sign in again", Busy = "Opening Bluesky…",
            Failure = Refused
        };

        /// <summary>The binding: the handle, with why it was refused under it and what stays below it, coming back here, and a
        /// refusal coming back with the handle typed; its ids under <paramref name="key"/>.</summary>
        public Form Binding(string key) => new(Action, HttpMethods.Post,
        [
            new("identifier", "", Handle)
            {
                Label = "Bluesky handle", Prefix = "@", Placeholder = "you.bsky.social", Required = true, Handle = true,
                Failure = SignInAgain is null ? Refused : null, Note = Render.Sentence.Plain(Note)
            },
            new("return", "", Return, Form.Input.Hidden),
            new("failure", "", Failure, Form.Input.Hidden) { Carries = ("identifier", HandleQuery) }
        ])
        {
            Tool = "another-account-form", Key = key, Island = "submitting", Submit = "Continue on Bluesky", Busy = "Opening Bluesky…", Quiet = SignInAgain is not null
        };
    }

    /// <summary>The form in the account menu of <paramref name="who"/>, coming back to the page they are on: the place's
    /// address and that page are the ones their sign-out names.</summary>
    public static Offer For(Who who)
    {
        var at = who.SignOut.IndexOf(AuthConstants.Routes.Logout, StringComparison.Ordinal);
        var address = at < 0 ? "" : who.SignOut[..at];
        var back = QueryHelpers.ParseQuery(new Uri(who.SignOut).Query).TryGetValue(AuthConstants.Query.Return, out var value) ? value.ToString() : "/";
        return new Offer(address + Route, back, FailurePath, who.Handle);
    }

    /// <summary>What Koan's refusal word says on the page; null for no word, and the publish grant's words for any other.</summary>
    public static string? Why(string? code) => code switch
    {
        null or "" => null,
        _ when code == SignInFailureReturn.Code(SignInFailure.SignInFirst) =>
            "Bluesky wants a fresh sign-in before you switch accounts. Sign in again, and you come back here to switch.",
        _ when code == SignInFailureReturn.Code(SignInFailure.LoginTaken) =>
            "That account already signed in here as someone else, so it stays theirs. Nothing was merged.",
        _ when code == SignInFailureReturn.Code(SignInFailure.HandleUnresolved) => "No Bluesky account answers to this handle. Check its spelling and continue.",
        _ => Connections.PublishController.Why(code)
    };

    /// <summary>The handle a query carries, kept only where a field can hold it.</summary>
    public static string? Kept(string? handle)
        => handle is { Length: > 0 and <= SignIn.HandleLimit } && handle.All(c => c is > ' ' and <= '~') ? handle.TrimStart('@') : null;
}
