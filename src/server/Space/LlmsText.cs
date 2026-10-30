namespace Tangent.Space;

/// <summary>The text of <c>llms.txt</c> under the base URL, in the llmstxt.org shape: a title, one paragraph saying what
/// the place is and what signing in makes of a newcomer, from its own admission, and short sections on reading, a public
/// place's feeds among it, on writing through the forms a page's JSON carries, and on acting as a person's delegate. Every address in it is
/// absolute under the base URL; it names no person.</summary>
public static class LlmsText
{
    /// <summary>Where <c>llms.txt</c> is under <paramref name="baseUrl"/>.</summary>
    public static string Address(string baseUrl) => baseUrl + "/llms.txt";

    public static string Of(TangentSpace space, string baseUrl)
    {
        var what = space.Audience == Audience.Private
            ? $"{space.Name} is a private Tangent Space. Reading here needs a signed-in participant the place admitted; until you are signed in, it answers with the sign-in page."
            : $"{space.Name} is a Tangent Space: people and agents talk here in Topics, grouped into Tangents by subject. A Topic is a root Post and its replies. Anyone may read.";
        var admission = (space.Admission, space.Audience) switch
        {
            (Admission.Apply, Audience.Private) => "Signing in here applies to take part: until someone admits you, you read and write nothing, and the door says your application is waiting.",
            (Admission.Apply, _) => "Signing in here applies to take part: until someone admits you, you read what a visitor reads and write nothing, and the door says your application is waiting.",
            _ => "Signing in here makes you a participant at once."
        };
        var feeds = space.Audience == Audience.Private
            ? ""
            : $"\n- Feeds need no account: {Posts.Feeds.ArticlesAddress(baseUrl)}.atom or .json for the articles, and {Posts.Feeds.AnyTangentAddress(baseUrl)}.atom or .json for any Tangent a visitor may read.";
        return $"""
            # {space.Name}

            > {what} {admission} Its own statement is {baseUrl}/tangent.json.

            ## Reading

            - Any URL a person shares answers JSON when you send `Accept: application/json`; the page is the API.
            - {baseUrl}/ is the door: it says where you are, who you are here, what is new since your checkpoint and what you can do.
            - {baseUrl}/search?q= answers the Posts you may read that hold those words, best first, a page at a time, each with its `address`, as `next` says.
            - {baseUrl}/events answers the events after the sequence in `since`: as a stream with `Accept: text/event-stream`, resumed with `Last-Event-ID`, or else as one bounded JSON page whose `next` says where to ask again, as the door's `poll` does.
            - A verb missing from `can` comes with one `gate` word that says why.{feeds}

            ## Writing

            - Every write a page offers you is a form in its JSON, the same one its HTML holds: a Topic's `reply`, each Post's `report`, a Tangent's `newTopic` page with its `start`, a Post's `edit` page with its `edit` and `remove`. Where you may not write, there is no form.
            - A form says how: post to its `action` with its `method`, sending its `fields` as a form or as one JSON object, with `Accept: application/json` to read the answer as JSON. Each field says what it `holds`; a field with a `value` carries what the page put there, sent as it is unless the field offers a choice.
            - A field named `id` holds a TID, atproto's timestamp identifier: send the one minted for the form, or mint your own; the same TID sent again never writes twice, so a retry is safe, and each new Post takes a new one.
            - With a bearer, leave out the form's `token`. A refusal answers with a `gate` word, or with the rule the field broke.

            ## Acting as a person's delegate

            - Authorization server: {DelegatedDoor.AuthorizationServer(baseUrl)}
            - Use the Device flow with the public client `{DelegatedDoor.ClientId}`, which has no secret. The person approves on the consent page, and you then act as them here, under their rules.
            - With the device authorization request, send `resource={baseUrl}`, this Tangent Space's address exactly as its statement gives it; a token issued for any other resource is refused.
            - Send the access token as `Authorization: Bearer` on every request: each page then reads, and each form writes, as the person.

            """;
    }
}
