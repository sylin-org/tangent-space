using Microsoft.AspNetCore.WebUtilities;
using Tangent.Render;

namespace Tangent.Space;

/// <summary>First run as a resource, in the one state the request finds it. Every state carries the place's
/// <see cref="Name"/> (<see cref="TangentSpace.Unnamed"/> until first run names it), the <see cref="BaseUrl"/> the
/// operator configured with the <see cref="Setting"/> that holds it, and the <see cref="Door"/>. Before sign-in it
/// carries <see cref="Challenge"/>, the Koan connector's atproto challenge, which comes back here signed in; signed in,
/// <see cref="Form"/>, which establishes the place; once the place is established, for its operator and its
/// proprietor, <see cref="Pointers"/>, what the site places to point at it. <see cref="Who"/> is the handle of whoever
/// is signed in.</summary>
public sealed record FirstRun(
    string Name,
    string BaseUrl,
    string Setting,
    string Door,
    string? Who = null,
    string? Challenge = null,
    FirstRunForm? Form = null,
    IReadOnlyList<Pointer>? Pointers = null)
{
    /// <summary>The challenge's address without its query: where the handle form sends the handle.</summary>
    public string ChallengeAddress() => (Challenge ?? "").Split('?', 2)[0];

    /// <summary>The address the challenge comes back to, as the handle form carries it.</summary>
    public string ChallengeReturn()
        => Challenge is not null && QueryHelpers.ParseQuery(new Uri(Challenge).Query).TryGetValue("return", out var value)
            ? value.ToString()
            : "";
}

/// <summary>The form that establishes the place: <see cref="Action"/> takes <c>name</c>, <c>proprietor</c> (one of
/// <see cref="Choices"/>: <c>me</c>, or <c>someone</c> with that participant's <c>handle</c>) and the form's
/// <see cref="Token"/>. <see cref="Handle"/> is the signed-in person's verified handle, or empty; they become the
/// operator. After a refusal it keeps what was sent and says why: <see cref="NameFailure"/> and
/// <see cref="ProprietorFailure"/> under their fields, <see cref="Failure"/> above the form. <see cref="SignOut"/>
/// signs this person out and comes back here, for someone who meant another account.</summary>
public sealed record FirstRunForm(
    string Action,
    FormToken Token,
    string Handle,
    string SignOut,
    string? Name = null,
    string Proprietor = FirstRunForm.Me,
    string? ProprietorHandle = null,
    string? Failure = null,
    string? NameFailure = null,
    string? ProprietorFailure = null)
{
    public const string Me = "me";
    public const string Someone = "someone";

    public IReadOnlyList<string> Choices => [Me, Someone];
}
