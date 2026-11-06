namespace Tangent.Render;

/// <summary>What each field of a write and each part of an address holds, said once: a <see cref="Form.Field"/> reads it
/// and so does the <c>[Description]</c> a tool lists from a route's input, so a form and a tool say the same thing.</summary>
public static class Holds
{
    private const string Markdown = ", in Markdown, at most 64 KiB; an @ and the handle of a participant here, typed in it, mentions them, and so does [@their name](/p/their id), with the id a Post's mentions give.";

    private const string Stays = " Left out, it stays.";

    public const string Tangent = "The slug of a Tangent, as its address names it.";
    public const string Topic = "The slug of a Topic of that Tangent, as its address names it.";
    public const string Post = "The Id of a Post of that Topic, as its address names it.";
    public const string Handle = "A participant's handle, or their DID.";

    /// <summary>A write's Id where its sender need not mint one.</summary>
    public const string Id = "A TID, atproto's timestamp identifier, that names this Post; the same TID sent again never makes a second one. Left out, each send makes a new Post.";

    public const string ReplyBody = "The reply" + Markdown;
    public const string Parent = "The Id of the Post of this Topic the reply answers; empty, it answers the Topic.";
    public const string TopicTitle = "The Topic's title, at most 200 characters.";
    public const string TopicBody = "Its opening Post" + Markdown;
    public const string Cover = "The Id of the image that leads the article, as bringing an image in answers it; left out, the article has none.";

    public const string EditVersion = "The version of the Post the edit was made from: one made from another changes nothing and answers with the Post as it stands.";
    public const string EditBody = "The Post's words, which replace what it says now" + Markdown;
    public const string EditBodyOptional = " Empty, the Post it was taken on a tangent from speaks for it.";
    public const string EditTitle = "The Topic's title, at most 200 characters." + Stays;
    public const string EditCover = "The Id of the image that leads the article, as bringing an image in answers it; empty, it has none." + Stays;

    public const string TakenFrom = "The id of a Post this Topic takes on a tangent, when a reply would wander from its Topic: the Topic links to it and it to the Topic, and the opening Post may then be empty.";

    public const string Reason = "Anything the moderators should know, in a sentence of Markdown, or nothing; only the moderators read it.";

    public const string BroughtImage = "A PNG, JPEG or WebP image, at most 20 MiB: a file of a multipart form, or its bytes in base64 in one JSON object. The answer's id is a cover's value, and its reference is what a body's Markdown names it by: ![what it shows](reference).";

    public const string SeenThrough = "What you have seen through: with no topic, the sequence of the log the door's news.through names, so nothing up to it is new to you anywhere; with a topic, the sequence of that Topic its page's through names. It never moves back.";
    public const string SeenTopic = "The id of the Topic you have read, as its page gives it; left out, everything the door read through is seen.";

    public const string Words = "The words to look for, at most 200 characters; left out, nothing is searched.";
    public const string Number = "The number of the page, from one; left out, the first.";
    public const string After = "The cursor a page of this list names as its next; left out, the first.";
    public const string WindowBefore = "The sequence a window of this Topic names as its older.";
    public const string WindowAfter = "The sequence a window of this Topic names as its newer.";
    public const string PostsBefore = "The cursor a page of this profile names as its older; left out, the newest.";
    public const string HandleStart = "The start of a handle, to narrow the people to those it begins; left out, everyone.";

    public const string Origin = "The origin an open Offer offers as the place's address, as the Offer names it.";

    public const string TangentName = "The Tangent's name, 1 to 80 characters; its address derives from it and never changes.";
    public const string TangentDescription = "What it is for, at most 1000 characters; left out, none.";
    public const string TangentKind = "The kind a Topic started there takes: topic or article; left out, topic.";
    public const string TangentAudience = "Who reads it: public, everyone who reads the place, where members post, or private; left out, public.";
    public const string ChangedTangentName = "The Tangent's name, 1 to 80 characters. Its address never changes." + Stays;
    public const string ChangedTangentDescription = "What it is for, at most 1000 characters." + Stays;
    public const string ChangedTangentKind = "The kind a Topic started there takes: topic or article." + Stays;
    public const string ChangedTangentAudience = "Who reads it: public or private." + Stays;
    public const string TangentAccent = "amber, coral, jade, sky, iris or rose: the tone its tile wears wherever it is drawn; sent empty, the one its slug gives it." + Stays;
    public const string TangentImage = "The image its tile wears in place of its letter: the name of a bundled cover, as image takes it in change_appearance, or /media/ and the id of an image brought in here; sent empty, none." + Stays;
    public const string DeletedTopics = "How many Topics go with the Tangent, the count its settings read, which is what it holds when the delete lands; left out, none, so only an empty Tangent is deleted.";
    public const string TangentMove = "earlier or later, to move it one place among the Tangents; sent alone, it changes nothing else.";
    public const string AccessRole = "The word of a role listed here with the access in its offered.";
    public const string Joining = "closed, only as its managers let people in; join, anyone who takes part in the place joins at once; or ask, people ask and its managers decide.";
    public const string Member = "The id of the participant, as the people tab lists them.";
    public const string Letting = "in, to let them in, or out, to decline their ask or take them out.";
    public const string Find = "The start of a participant's handle.";
    public const string Access = "default, as its role allows in the place; read, reading alone; or post, reading and posting.";

    public const string PlaceName = "The place's name, 1 to 120 characters on one line." + Stays;
    public const string PlaceDescription = "Markdown under the place's name on the door, at most 300 characters; its words are what a link to the door says of it." + Stays;
    public const string PlaceAudience = "public, where anyone reads the place, or private, where its participants alone do and a visitor finds the sign-in." + Stays;
    public const string Admission = "join, where a newcomer who signs in is admitted at once, or apply, where they wait to be admitted; everyone already here stays as they are.";
    public const string Base = "What styles the plain elements and sets the fonts, the base layer: tangent, Tangent's own, or a bundled classless stylesheet: simple, mvp, marx, sakura, water or new; or concord, Tangent's own arrangement of the place in a chat app's look." + Stays;
    public const string Palette = "The place's colours: base, the base's own, which under Tangent's own base is Tangent's palette in gold; or gold, jade, iris or copper, Tangent's palette in that accent over any base. Links, buttons and what is new take the accent on every page, for every reader." + Stays;
    public const string Css = "The place's own CSS, the owner layer, over every other layer on every page but the settings, at most 50,000 characters; sent empty, none." + Stays;
    public const string PlaceImage = "The image behind the door's head and on a card a link to the door shows: the name of a bundled cover (the-commons, midnight-workshop, universe-within, quiet-geometry, paper-current, prism-study, blueprint-exchange, greenhouse, moss-and-signal, coastal-hours, orbital-dawn, neural-loom, companion-circle, afterlight, arcade-bloom or electric-ink), or /media/ and the id of an image brought in here; sent empty, none." + Stays;
    public const string PlaceMark = "The place's mark, beside its name in every masthead and every page's icon: /media/ and the id of an image brought in here, drawn square; sent empty, its letter." + Stays;
    public const string From = "bluesky, to bring in the picture your Bluesky account shows: its banner as the place's image, its avatar as the place's mark.";
    public const string Return = "Where a page sends you once the write is done: a path of this place, as /; left out, the page the write belongs to.";
    public const string Masthead = "Markdown beside the place's name on every page, written as a Post is, at most 2000 characters." + Stays;
    public const string Footer = "Markdown first in every page's footer, written as a Post is, at most 2000 characters." + Stays;
    public const string Intro = "Markdown under the door's head, the place's welcome, written as a Post is, at most 8000 characters." + Stays;
    public const string ArticleHeader = "Markdown above every article's title, written as a Post is, at most 8000 characters." + Stays;

    public const string Applicant = "The id of an applicant this page lists.";
    public const string Participant = "The id of a participant the people list.";
    public const string TimeOutFor = "How long the timeout lasts: hour, day or week.";
    public const string Back = "The page a form sent from a page returns to; a JSON request reads the participant as the people list them instead.";
    public const string GivenRole = "The role to give, by its word, one in their give.";
    public const string TakenRole = "The role to take, by its word, one in their take.";

    public const string RoleName = "The role's name, 1 to 80 characters; its word derives from it and never changes.";
    public const string RolePermissions = "The bits it grants, by name, separated by spaces or one to an item, among those whose held is true; sent empty, none.";
    public const string ChangedRole = "The word of a role listed here with change in its can.";
    public const string ChangedRoleName = "Its name, 1 to 80 characters." + Stays;
    public const string RoleMove = "higher or lower, one place, where its can holds that word." + Stays;
    public const string DeletedRole = "The word of a role listed here with delete in its can.";
}
