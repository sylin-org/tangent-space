using Microsoft.AspNetCore.WebUtilities;
using Tangent.Render;

namespace Tangent.Space;

/// <summary>First run as a resource, in the one state the request finds it. Every state carries the place's
/// <see cref="Name"/> (<see cref="TangentSpace.Unnamed"/> until first run names it), the <see cref="BaseUrl"/> the
/// operator configured with the <see cref="Setting"/> that holds it, the <see cref="Door"/>, and who may set the place
/// up: the person whose DID is <see cref="Operator"/> when the configuration names one, otherwise whoever has the setup
/// code the server wrote to its log. Before sign-in it carries <see cref="Challenge"/>, the Koan connector's atproto
/// challenge, which comes back here signed in; signed in as someone who may set it up, <see cref="Form"/>, which
/// establishes the place; signed in as anyone else, <see cref="Waiting"/>; once the place is established, for its
/// operator and its proprietor, <see cref="Pointers"/>, what the site places to point at it. <see cref="Reader"/> is
/// whoever is signed in, as the account menu shows them, with where they sign out.</summary>
public sealed record FirstRun(
    string Name,
    string BaseUrl,
    string Setting,
    string Door,
    string? Operator = null,
    string? Challenge = null,
    FirstRunForm? Form = null,
    FirstRunWaiting? Waiting = null,
    IReadOnlyList<Pointer>? Pointers = null,
    FirstRunReader? Reader = null)
{
    /// <summary>The configuration key that names the operator.</summary>
    public const string OperatorSetting = "Tangent:Operator";

    /// <summary>The challenge's address without its query: where the handle form sends the handle.</summary>
    public string ChallengeAddress() => (Challenge ?? "").Split('?', 2)[0];

    /// <summary>The address the challenge comes back to, as the handle form carries it.</summary>
    public string ChallengeReturn()
        => Challenge is not null && QueryHelpers.ParseQuery(new Uri(Challenge).Query).TryGetValue("return", out var value)
            ? value.ToString()
            : "";
}

/// <summary>The form that establishes the place: <see cref="Action"/> takes <c>name</c>, <c>proprietor</c> (one of
/// <see cref="Choices"/>: <c>me</c>, or <c>someone</c> with that participant's <c>handle</c>), <c>accountable</c>, the
/// sender's statement that they are the person who answers for the place, <c>code</c>, the setup code, where
/// <see cref="AsksCode"/>, and the form's <see cref="Token"/>. <see cref="Handle"/> is the signed-in person's verified
/// handle, or empty; they become the operator. After a refusal it keeps what was sent, never the code, and says why:
/// each field's failure under it, <see cref="Failure"/> above the form.</summary>
public sealed record FirstRunForm(
    string Action,
    FormToken Token,
    string Handle,
    bool AsksCode,
    string? Name = null,
    string Proprietor = FirstRunForm.Me,
    string? ProprietorHandle = null,
    bool Accountable = false,
    string? Failure = null,
    string? CodeFailure = null,
    string? NameFailure = null,
    string? ProprietorFailure = null,
    string? AccountableFailure = null)
{
    public const string Me = "me";
    public const string Someone = "someone";

    public IReadOnlyList<string> Choices => [Me, Someone];
}

/// <summary>A signed-in person who may not set the place up, since the configuration names another operator: they
/// signed in as <see cref="Handle"/> (empty when the connector verified none).</summary>
public sealed record FirstRunWaiting(string Handle);

/// <summary>The signed-in person reading first run: the name to show, their handle, the highest role they hold where
/// they hold one, their avatar, and where they sign out, coming back to first run.</summary>
public sealed record FirstRunReader(string Id, string Name, string Handle, string? Role, string? Avatar, string SignOut);
