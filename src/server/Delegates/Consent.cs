using Tangent.Render;

namespace Tangent.Delegates;

/// <summary>The consent page as a resource, in the one state the request finds it: <see cref="Entry"/>, the field
/// for the code a device or agent shows; <see cref="Decision"/>, the request waiting under that code or under the
/// loopback client's request, with the forms that approve and deny it; or <see cref="Done"/>, where Koan's server
/// sends the person once they have decided, with <see cref="Approved"/> saying what they decided as Koan read it: true
/// or false for the person who decided, and null for anyone else or an address Koan did not write. Every state carries
/// the place's name, its door and <see cref="Reader"/>, the signed-in person who decides, as the account menu shows
/// them.</summary>
public sealed record Consent(
    string Place,
    string Door,
    Who? Reader,
    ConsentEntry? Entry = null,
    ConsentDecision? Decision = null,
    bool Done = false,
    bool? Approved = null);

/// <summary>The form that asks for a device's code: it is sent to <see cref="Action"/> as <see cref="Field"/>.
/// After a code that no request is waiting under, <see cref="Code"/> keeps what was typed and <see cref="Failure"/>
/// says why, under the field.</summary>
public sealed record ConsentEntry(string Action, string Field, string? Code = null, string? Failure = null);

/// <summary>One request waiting for the person's decision: <see cref="Approve"/> and <see cref="Deny"/> are the forms that
/// post to Koan's request endpoint, each taking nothing but its token, in the person's own session. A request that came
/// by the Device flow carries
/// its <see cref="Code"/> and never a client's name: only the shared pre-registered client is allowed to ask that way,
/// and the page cannot know who holds it. A loopback client's request carries <see cref="Client"/>, the name it registered, and
/// <see cref="Redirect"/>, where it returns.</summary>
public sealed record ConsentDecision(
    Form Approve,
    Form Deny,
    string? Code = null,
    string? Client = null,
    string? Redirect = null);
