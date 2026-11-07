namespace Tangent.Render;

/// <summary>A participant as a page shows them, the one part for a person or an agent wherever one stands: their id, the
/// handle and name shown, their kind (<c>person</c> or <c>agent</c>), their avatar, and their profile's address where the page
/// links them. <see cref="Account"/> is the DID a Post was written as, where the person stands as a Post's author: their card
/// links out to it in place of the account they hold now.</summary>
public sealed record Person(string Id, string Handle, string Name, string Kind, string? Avatar, string? Profile = null) : IPart
{
    /// <summary>The account a Post was written as, where this is its author's mark; the page's JSON says it on the Post.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Account { get; init; }

    /// <summary>The highest role they hold, where the place holding them says it beside their handle.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Role { get; init; }

    /// <summary>Whether the participant is an agent.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool Agent => Kind == "agent";

    /// <summary>A participant as a page shows them, their name linked to their profile under <paramref name="baseAddress"/>
    /// where one is given.</summary>
    public static Person Of(Participants.Participant row, string? baseAddress = null)
        => new(row.Id, row.ShownHandle(), row.ShownName(), row.Kind.ToString().ToLowerInvariant(), row.Profile.Avatar,
            baseAddress is null ? null : Participants.ProfilePage.AddressOf(baseAddress, row.ShownHandle()));

    /// <summary>How someone who left the place reads wherever they are named.</summary>
    public const string LeftName = "Someone who left";

    /// <summary>Someone who left the place, as a page names whoever its id no longer reaches: by those words, a plain mark,
    /// no profile and no card. As a Post's author, their name links out to the account it was written as
    /// (<see cref="Account"/>).</summary>
    public static Person Left(string id) => new(id, "", LeftName, "person", null);
}
