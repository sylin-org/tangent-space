namespace Tangent.Render;

/// <summary>What each field of a write and each part of an address holds, said once: a <see cref="Form.Field"/> reads it
/// and so does the <c>[Description]</c> a tool lists from a route's input, so a form and a tool say the same thing.</summary>
public static class Holds
{
    private const string Markdown = ", in Markdown, at most 64 KiB; an @ and the handle of a participant here, typed in it, mentions them.";

    private const string Stays = " Left out, it stays.";

    public const string Tangent = "The slug of a Tangent, as its address names it.";
    public const string Topic = "The slug of a Topic of that Tangent, as its address names it.";
    public const string Post = "The Id of a Post of that Topic, as its address names it.";
    public const string Handle = "A participant's handle, or their DID.";

    /// <summary>A write's Id where its sender need not mint one.</summary>
    public const string Id = "A TID, atproto's timestamp identifier, that names this Post; the same TID sent again never makes a second one. Left out, the words themselves name it for a few minutes: sent again by then, they are the one Post, and sent later, a new one.";

    public const string ReplyBody = "The reply" + Markdown;
    public const string Parent = "The Id of the Post of this Topic the reply answers; empty, it answers the Topic.";
    public const string TopicTitle = "The Topic's title, at most 200 characters.";
    public const string TopicBody = "Its opening Post" + Markdown;
    public const string Kind = "topic or article; left out, the Tangent's default.";
    public const string Cover = "The Id of the image that leads the article, as bringing an image in answers it; left out, the article has none.";

    public const string EditVersion = "The version of the Post the edit was made from: one made from another changes nothing and answers with the Post as it stands.";
    public const string EditBody = "The Post's words, which replace what it says now" + Markdown;
    public const string EditBodyOptional = " Empty, the Post it was taken on a tangent from speaks for it.";
    public const string EditTitle = "The Topic's title, at most 200 characters." + Stays;
    public const string EditCover = "The Id of the image that leads the article, as bringing an image in answers it; empty, it has none." + Stays;

    public const string TakeTangent = "The slug of the Tangent the new Topic starts in.";
    public const string TakeFrom = "The slug of the Tangent the Post is in, as its address names it.";
    public const string TakeTitle = "The new Topic's title, at most 200 characters.";
    public const string TakeBody = "The taker's own words above the Post taken on a tangent" + Markdown + " It may be empty.";

    public const string Reason = "The reason, in a sentence or two of Markdown; only the moderators read it.";

    public const string BroughtImage = "A PNG, JPEG or WebP image, at most 20 MiB: a file of a multipart form, or its bytes in base64 in one JSON object. The answer's id is a cover's value, and its reference is what a body's Markdown names it by: ![what it shows](reference).";

    public const string TopicThrough = "The sequence of this Topic the reader has seen through: the last the page shows, or any other of the Topic. The checkpoint never moves back.";
    public const string DoorThrough = "The sequence of the log the door read through; the reader's place checkpoint moves there and never back.";
    public const string Watch = "true to watch it, false to stop.";

    public const string Words = "The words to look for, at most 200 characters; left out, nothing is searched.";
    public const string Number = "The number of the page, from one; left out, the first.";
    public const string After = "The cursor a page of this list names as its next; left out, the first.";
    public const string WindowBefore = "The sequence a window of this Topic names as its older.";
    public const string WindowAfter = "The sequence a window of this Topic names as its newer.";
    public const string PostsBefore = "The cursor a page of this profile names as its older; left out, the newest.";
    public const string AgentsPage = "The number of a page of the agents acting as you, from one, on your own profile.";
    public const string HandleStart = "The start of a handle, to narrow the people to those it begins; left out, everyone.";

    public const string Offer = "The Offer's Id.";

    public const string TangentName = "The Tangent's name, 1 to 80 characters; its address derives from it and never changes.";
    public const string TangentDescription = "What it is for, at most 1000 characters; left out, none.";
    public const string TangentKind = "The kind a Topic started there takes: topic or article; left out, topic.";
    public const string TangentAudience = "Who reads it: public, everyone who reads the place, where members post, or private; left out, public.";
    public const string ChangedTangentName = "The Tangent's name, 1 to 80 characters. Its address never changes." + Stays;
    public const string ChangedTangentDescription = "What it is for, at most 1000 characters." + Stays;
    public const string ChangedTangentKind = "The kind a Topic started there takes: topic or article." + Stays;
    public const string ChangedTangentAudience = "Who reads it: public or private." + Stays;
    public const string TangentMove = "earlier or later, to move it one place among the Tangents; sent alone, it changes nothing else.";
    public const string AccessRole = "The word of a role listed here with the access in its offered.";
    public const string Access = "default, as its role allows in the place; read, reading alone; or post, reading and posting.";

    public const string PlaceName = "The place's name, 1 to 120 characters on one line." + Stays;
    public const string PlaceDescription = "The line under its name on the door, at most 300 characters." + Stays;
    public const string PlaceAudience = "public, where anyone reads the place, or private, where its participants alone do and a visitor finds the sign-in." + Stays;
    public const string Admission = "join, where a newcomer who signs in is admitted at once, or apply, where they wait to be admitted; everyone already here stays as they are.";
    public const string Accent = "gold, jade, iris or copper: the colour links, buttons and what is new take on every page, for every reader." + Stays;
    public const string HeadSlot = "HTML for every page's head, sanitized to meta and icon links, at most 4000 characters." + Stays;
    public const string MastheadSlot = "HTML beside the place's name on every page, sanitized to a line of links, images and words, at most 2000 characters." + Stays;
    public const string DoorIntroSlot = "HTML under the door's head, sanitized to prose, at most 8000 characters." + Stays;
    public const string ArticleHeaderSlot = "HTML above every article's title, sanitized to prose, at most 8000 characters." + Stays;
    public const string FooterSlot = "HTML in every page's footer, sanitized to a line of links, images and words, at most 2000 characters." + Stays;

    public const string Applicant = "The id of an applicant this page lists.";
    public const string Participant = "The id of a participant the people list.";
    public const string TimeOutFor = "How long the timeout lasts: hour, day or week.";
    public const string Back = "The page a form sent from a page returns to; a JSON request reads the participant as the people list them instead.";
    public const string GivenRole = "The role to give, by its word, one in their give.";
    public const string TakenRole = "The role to take, by its word, one in their take.";

    public const string RoleName = "The role's name, 1 to 80 characters; its word derives from it and never changes.";
    public const string RolePermissions = "The bits it grants, by name, separated by spaces or one to an item, among those whose held is true; sent as one empty string, none.";
    public const string ChangedRole = "The word of a role listed here with change in its can.";
    public const string ChangedRoleName = "Its name, 1 to 80 characters." + Stays;
    public const string RoleMove = "higher or lower, one place, where its can holds that word." + Stays;
    public const string DeletedRole = "The word of a role listed here with delete in its can.";
}
