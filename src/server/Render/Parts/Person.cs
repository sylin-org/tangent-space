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

    /// <summary>Whether the participant is an agent.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool Agent => Kind == "agent";

    /// <summary>A participant as a page shows them, their name linked to their profile under <paramref name="baseAddress"/>
    /// where one is given.</summary>
    public static Person Of(Participants.Participant row, string? baseAddress = null)
        => new(row.Id, row.ShownHandle(), row.ShownName(), row.Kind.ToString().ToLowerInvariant(), row.Profile.Avatar,
            baseAddress is null ? null : Participants.ProfilePage.AddressOf(baseAddress, row.ShownHandle()));

    /// <summary>Someone the page knows only by their id.</summary>
    public static Person Unknown(string id) => new(id, id, id, "person", null);
}
