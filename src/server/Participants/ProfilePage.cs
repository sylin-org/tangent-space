using System.Security.Cryptography;
using System.Text.Json;
using Tangent.Render;

namespace Tangent.Participants;

/// <summary>A participant's profile as a resource, the same for a person and an agent: who they are as their PDS says —
/// <see cref="Handle"/>, <see cref="DisplayName"/>, <see cref="Description"/> and <see cref="Avatar"/>, with
/// <see cref="Name"/> the one to show — their <see cref="Kind"/>, the <see cref="Roles"/> they hold here by name, when
/// they <see cref="Arrived"/>, and one bounded page of their <see cref="Posts"/> newest first, only those in Tangents the
/// reader may view. <see cref="Older"/> is the address of the page after this one, absent on the last; nothing is counted
/// to answer it. <see cref="Can"/> is what the reader holds on the place and <see cref="Gate"/> the one reason they may
/// not write here, where they may not. <see cref="Delegates"/> are the agents the reader approved to act as them, present
/// only on their own profile, read in their own session. <see cref="Place"/>, <see cref="Door"/>, <see cref="Who"/> and
/// <see cref="SignIn"/> say where the page is and who reads it; <see cref="Day"/> is the UTC day its times are written
/// for.</summary>
public sealed record ProfilePage(
    string Id,
    string Handle,
    string Name,
    string? DisplayName,
    string? Description,
    string? Avatar,
    string Kind,
    IReadOnlyList<string> Roles,
    DateTimeOffset? Arrived,
    string Address,
    IReadOnlyList<string> Can,
    string? Gate,
    IReadOnlyList<ProfilePage.Written> Posts,
    string? Older,
    bool Continued,
    ProfilePage.Approvals? Delegates,
    string Place,
    string Door,
    string? SignIn,
    Who? Who,
    DateOnly Day)
{
    /// <summary>The URL segment profiles answer under.</summary>
    public const string Segment = "/p/";

    /// <summary>One Post the participant wrote: a Topic's root (<c>topic</c> or <c>article</c>) or a <c>reply</c>, the
    /// title of its Topic, the Tangent it is in, its address, its first words and when it was written.</summary>
    public sealed record Written(string Id, string Kind, string Topic, string Tangent, string TangentAddress, string Address, string Line,
        DateTimeOffset At);

    /// <summary>The agents the reader approved on the consent page that may still act as them, one per approval, newest
    /// first, each with its client's name, when they approved it and the form that revokes it; <see cref="Lasting"/> is how long an agent keeps the access it
    /// already holds once its approval is revoked, and <see cref="Token"/> the forms' antiforgery token.</summary>
    public sealed record Approvals(IReadOnlyList<Approval> Agents, TimeSpan Lasting, FormToken Token);

    /// <summary>One approval: its id; its client's name, what the client registered, or its client id where it registered
    /// none, which is <see cref="SelfNamed"/> where a client registered itself rather than being this place's own shared
    /// client; when it was given; and where its revoke form posts.</summary>
    public sealed record Approval(string Id, string Client, bool SelfNamed, DateTimeOffset? Approved, string Revoke);

    /// <summary>A participant's profile address under the base URL in force: by their handle, or by their DID where they have
    /// none.</summary>
    public static string AddressOf(string baseAddress, string handle) => baseAddress + Segment + Uri.EscapeDataString(handle);

    /// <summary>The version the page's <c>ETag</c> carries: a hash of everything the page says, so a new Post, a changed
    /// profile or role, a changed <c>can</c> or a new day changes it. The reader's own profile carries the revoke forms
    /// with a new antiforgery token every time, so its version never repeats.</summary>
    public static long Version(ProfilePage page)
        => BitConverter.ToInt64(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(page)), 0) & long.MaxValue;
}
