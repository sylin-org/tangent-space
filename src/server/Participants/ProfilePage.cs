using System.Security.Cryptography;
using System.Text.Json;
using Tangent.Render;

namespace Tangent.Participants;

/// <summary>A participant's profile as a resource, the same for a person and an agent: who they are as the Bluesky account
/// they are bound to now (<see cref="Did"/>) says — <see cref="Handle"/>, <see cref="DisplayName"/>, <see cref="Description"/> and <see cref="Avatar"/>, with
/// <see cref="Name"/> the one to show — their <see cref="Kind"/>, the <see cref="Roles"/> they hold here by name, when
/// they <see cref="Arrived"/>, and one bounded page of their <see cref="Posts"/> newest first, only those in Tangents the
/// reader may view. <see cref="Older"/> is the address of the page after this one, absent on the last; nothing is counted
/// to answer it. <see cref="Can"/> is what the reader holds on the place and <see cref="Gate"/> the one reason they may
/// not write here, where they may not. For a reader who holds <c>ManageStanding</c> or
/// <c>ManageRoles</c>, <see cref="Listed"/> is the participant as the place's people list them — their standing, with when
/// a timeout ends, their roles and what the reader may change of them — and <see cref="Forms"/> where each change posts,
/// coming back here; both are absent for anyone else. <see cref="Place"/>, <see cref="Door"/>, <see cref="Who"/> and
/// <see cref="SignIn"/> say where the page is and who reads it; <see cref="Day"/> is the UTC day its times are written
/// for.</summary>
public sealed record ProfilePage(
    string Id,
    string Did,
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
    string Place,
    string Door,
    string? SignIn,
    Who? Who,
    DateOnly Day,
    PeoplePage.Listed? Listed = null,
    PeopleForms? Forms = null)
{
    /// <summary>The URL segment profiles answer under.</summary>
    public const string Segment = "/p/";

    /// <summary>One Post the participant wrote: a Topic's root (<c>topic</c> or <c>article</c>) or a <c>reply</c>, the
    /// title of its Topic, the Tangent it is in, its address, its first words, when it was written, and who other than
    /// the participant last edited it, where someone did.</summary>
    public sealed record Written(string Id, string Kind, string Topic, string Tangent, string TangentAddress, string Address, string Line,
        DateTimeOffset At, Tangents.TangentPage.Edited? Edited = null);

    /// <summary>A participant's profile address under the base URL in force: by their handle, or by their DID where they have
    /// none.</summary>
    public static string AddressOf(string baseAddress, string handle) => baseAddress + Segment + Uri.EscapeDataString(handle);

    /// <summary>The version the page's <c>ETag</c> carries: a hash of everything the page says, so a new Post, a changed
    /// profile or role, a changed <c>can</c> or a new day changes it.</summary>
    public static long Version(ProfilePage page)
        => BitConverter.ToInt64(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(page)), 0) & long.MaxValue;
}
