using Microsoft.AspNetCore.WebUtilities;
using Tangent.Render;

namespace Tangent.Space;

/// <summary>First run as a resource, in the one state the request finds it. Every state carries the place's
/// <see cref="Name"/> (<see cref="TangentSpace.Unnamed"/> until first run names it), the <see cref="Door"/>, and, where
/// the configuration names the account that may set the place up, that account as <see cref="Operator"/>: its handle
/// without the <c>@</c>, or its DID; with none named, the first person to sign in sets it up. For a visitor, and for
/// someone signed in who may set it up, it carries <see cref="Challenge"/>, the Koan connector's atproto challenge,
/// whose sign-in sets the place up and comes back here; signed in as anyone else, <see cref="Waiting"/>; once the place
/// is established, for its operator and its proprietor, the <see cref="BaseUrl"/> it answers at and the
/// <see cref="Pointers"/>, what the site places to point at it. <see cref="Reader"/> is whoever is
/// signed in, as the account menu shows them, with where they sign out.</summary>
public sealed record FirstRun(
    string Name,
    string Door,
    string? Operator = null,
    string? Challenge = null,
    FirstRunWaiting? Waiting = null,
    string? BaseUrl = null,
    IReadOnlyList<Pointer>? Pointers = null,
    FirstRunReader? Reader = null)
{
    /// <summary>The configuration key that names the account that may set the place up.</summary>
    public const string OperatorSetting = "Tangent:Operator";

    /// <summary>The challenge's address without its query: where the sign-in forms go.</summary>
    public string ChallengeAddress() => (Challenge ?? "").Split('?', 2)[0];

    /// <summary>The address the challenge comes back to, as the sign-in forms carry it.</summary>
    public string ChallengeReturn()
        => Challenge is not null && QueryHelpers.ParseQuery(new Uri(Challenge).Query).TryGetValue("return", out var value)
            ? value.ToString()
            : "";
}

/// <summary>A signed-in person who may not set the place up, since the configuration names another account: they
/// signed in as <see cref="Handle"/> (empty when the connector verified none), and <see cref="OperatorHandle"/> is the
/// named account's handle: the configured one, or, for a configured DID, the handle of the participant with that DID
/// where one has arrived here.</summary>
public sealed record FirstRunWaiting(string Handle, string? OperatorHandle = null);

/// <summary>The signed-in person reading first run: the name to show, their handle, the highest role they hold where
/// they hold one, their avatar, and where they sign out, coming back to first run.</summary>
public sealed record FirstRunReader(string Id, string Name, string Handle, string? Role, string? Avatar, string SignOut);
