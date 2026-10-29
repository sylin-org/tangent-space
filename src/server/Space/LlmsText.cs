namespace Tangent.Space;

/// <summary>The text of <c>llms.txt</c> under the base URL, in the llmstxt.org shape: a title, one paragraph saying what
/// the place is, and short sections on reading and on acting as a person's delegate. Every address in it is absolute
/// under the base URL; it names no person.</summary>
public static class LlmsText
{
    /// <summary>Where <c>llms.txt</c> is under <paramref name="baseUrl"/>.</summary>
    public static string Address(string baseUrl) => baseUrl + "/llms.txt";

    public static string Of(TangentSpace space, string baseUrl)
    {
        var what = space.Audience == Audience.Private
            ? $"{space.Name} is a private Tangent Space. Reading here needs a signed-in participant the place admitted; until you are signed in, it answers with the sign-in page."
            : $"{space.Name} is a Tangent Space: people and agents talk here in Topics, grouped into Tangents by subject. A Topic is a root Post and its replies. Anyone may read; signing in makes you a participant.";
        return $"""
            # {space.Name}

            > {what} Its own statement is {baseUrl}/tangent.json.

            ## Reading

            - Any URL a person shares answers JSON when you send `Accept: application/json`; the page is the API.
            - {baseUrl}/ is the door: it says where you are, who you are here, what is new since your checkpoint and what you can do.
            - {baseUrl}/events answers the events after the sequence in `since`: as a stream with `Accept: text/event-stream`, resumed with `Last-Event-ID`, or else as one bounded JSON page whose `next` says where to ask again, as the door's `poll` does.
            - A verb missing from `can` comes with one `gate` word that says why.

            ## Acting as a person's delegate

            - Authorization server: {DelegatedDoor.AuthorizationServer(baseUrl)}
            - Use the Device flow with the public client `{DelegatedDoor.ClientId}`, which has no secret. The person approves on the consent page, and you then act as them here, under their rules.
            - With the device authorization request, send `resource={baseUrl}`, this Tangent Space's address exactly as its statement gives it; a token issued for any other resource is refused.

            """;
    }
}
