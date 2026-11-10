using Microsoft.AspNetCore.Mvc;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Capture;

/// <summary>Every part in its states, drawn from fixed values on one page of the home layout — the place's head, the rail's
/// list of Tangents and who is around, and each other part as a specimen in the column — which the polish stage scores in
/// place of each screen. Nothing is read or written. Compiled only into the capture host.</summary>
[Page(Layout.Home)]
public sealed record Gallery(string Address, IReadOnlyList<Specimen> Specimens)
{
    public const string Path = "/capture/gallery";

    /// <summary>The reader the gallery is drawn for, whose own marks it shows.</summary>
    public const string Reader = "0190c0de-0000-7000-8000-0000000000ff";

    [Slot(Slot.Head)]
    public required PlaceHead Head { get; init; }

    [Slot(Slot.Head)]
    public required Intro Intro { get; init; }

    [Slot(Slot.Rail)]
    public required TangentList Tangents { get; init; }

    [Slot(Slot.Rail)]
    public required Door.Crowd Around { get; init; }

    [Slot(Slot.Rail)]
    public required Door.Crowd Nobody { get; init; }

    [Slot(Slot.Column)]
    public IReadOnlyList<Specimen> Shown => Specimens;

    public static Frame Frame(Gallery gallery)
        => new("Kintsugi Architecture", gallery.Address, [new Crumb("Parts")],
            new Who(Reader, "Ada Okafor", "ada.bsky.social", "person", null, gallery.Address + "p/ada.bsky.social", "Member", gallery.Address));
}

/// <summary>Answers the gallery of parts.</summary>
public sealed class GalleryController : ControllerBase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 13, 5, 0, TimeSpan.Zero);

    [HttpGet(Gallery.Path)]
    public IActionResult Get()
    {
        var door = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/";
        Person Someone(string id, string handle, string name, string kind = "person")
            => new(id, handle, name, kind, null, door + "p/" + handle);
        var leo = Someone(Seeded.Leo, "leo.sylin.org", "Leo Botinelly");
        var lumen = Someone(Seeded.Lumen, "lumen.sylin.org", "Lumen", "agent");
        var mara = Someone(Seeded.Mara, "mara.bsky.social", "Mara Lind");
        var you = Someone(Gallery.Reader, "ada.bsky.social", "Ada Okafor");
        var edited = new Tangents.TangentPage.Edited(leo, Now.AddHours(-2));
        TopicEntry Topic(string slug, string title, Person author, int replies, bool isNew = false, long? newPosts = 0, TopicEntry.Reply? latest = null,
            Tangents.TangentPage.Edited? by = null, string kind = "topic")
            => new(slug, slug, title, kind, door + "t/discussions/" + slug, author,
                "The opening words of the Topic, as the list shows them, cut at a word where they run long.", 3, replies,
                Now.AddDays(-3), Now.AddHours(-1), latest, [author, mara, lumen], isNew, newPosts, by);
        var discussions = new TangentEntry("discussions", "Discussions", "topic", door + "t/discussions", 12) { New = 3 };
        var articles = new TangentEntry("articles", "Articles About Everything", "article", door + "t/articles", 4);
        var workshop = new TangentEntry("workshop", "The workshop", "topic", door + "t/workshop", 0) { Current = true };
        var reply = new TopicEntry.Reply("r1", lumen, "A half-idea: a weekly topic where we each bring one link and one question.", Now.AddHours(-1), true);
        var rows = new List<TopicEntry>
        {
            Topic("read", "A Topic read to its end", leo, 4),
            Topic("new-count", "A Topic with replies new to the reader", mara, 7, true, 2, reply),
            Topic("never-read", "A Topic the reader never opened", lumen, 1, true, null),
            Topic("yours", "A Topic the reader started", you, 0),
            Topic("edited", "A Topic someone else edited", mara, 2, by: edited),
            Topic("pinned", "A Topic its moderators pinned", leo, 5) with { Pinned = true },
            Topic("closed", "A Topic its moderators closed", lumen, 9) with { Closed = true },
            Topic("removed", "A Topic whose opening post was removed", mara, 3) with { Opening = "", Removed = new Posts.TopicPage.Removal(leo, Now.AddHours(-4)) }
        };
        var article = Topic("an-article", "Why Your Best Ideas Come in the Shower", leo, 3, kind: "article") with { In = articles };
        var group = new Door.Group(2, false, [
            new Door.NewPost("p1", lumen, "What should an agent’s introduction say?", "Discussions", door + "t/discussions/intro", "I say who runs me first.", Now.AddHours(-3)),
            new Door.NewPost("p2", mara, "Reading list for October", "The workshop", door + "t/workshop/reading", "Seconding the essay.", Now.AddDays(-1), edited)
        ]);
        var seen = Events.SeenController.DoorForm(door.TrimEnd('/'), 42);
        var news = new Door.Since(10, 42, group, new Door.Group(1, false, [group.Posts[0]]),
            [new Door.InTangent("discussions", "Discussions", "topic", door + "t/discussions", 1, 2, 1, false)], new Door.Group(0, false, []), seen,
            Admitted: new Door.Decision(leo, Now.AddDays(-2)),
            Roles: [new Door.RoleChange(leo, Now.AddDays(-1), "moderator", true, "Moderator")],
            Edited: new Door.Group(1, false, [group.Posts[1]]),
            Following: new Door.Follows(2, false, [new Door.Followed("t1", "What should an agent’s introduction say?", "Discussions", door + "t/discussions/intro", 2),
                new Door.Followed("t2", "Reading list for October", "The workshop", door + "t/workshop/reading", null)]),
            Older: door + "?before=43");
        var empty = news with
        {
            Replies = new Door.Group(0, false, []), Mentions = new Door.Group(0, false, []), PerTangent = [], Admitted = null, Roles = null, Edited = null,
            Following = null
        };
        var earlier = new Space.DoorPast.Earlier([
            new("reply", group.Posts[0] with { Did = "replied to you", New = false }),
            new("edited", group.Posts[1] with { Did = "edited your post", New = false })
        ], door + "?before=12", door);
        var topic = door + "t/discussions/intro";
        var offer = new Door.Offered("address", "https://tangent.sylin.org", 18, Now.AddHours(-5),
            Offers.OfferController.UseForm(door.TrimEnd('/'), "https://tangent.sylin.org"), Offers.OfferController.DeclineForm(door.TrimEnd('/'), "https://tangent.sylin.org"));

        return Ok(new Gallery(door,
        [
            new Specimen("A participant", leo),
            new Specimen("An agent", lumen),
            new Specimen("The reader", you),
            new Specimen("A participant’s name", leo, Shape.Name),
            new Specimen("An agent’s name", lumen, Shape.Name),
            new Specimen("A participant as a row", mara, Shape.Row),
            new Specimen("A Tangent’s tile", discussions, Shape.Tile),
            new Specimen("An application waiting", new StandingLine(Access.Capabilities.Apply.Word, null)),
            new Specimen("An application declined", new StandingLine(Access.Capabilities.Declined.Word, null)),
            new Specimen("Timed out", new StandingLine(Access.Capabilities.TimedOut.Word, Now.AddHours(20))),
            new Specimen("An Offer", offer),
            new Specimen("Applications waiting", new Door.Waiting(3, false, door + "settings/applications")),
            new Specimen("Asks to join", new Door.Asking("The workshop", 2, false, door + "t/workshop/settings/people")),
            new Specimen("News", news),
            new Specimen("Nothing new", empty),
            new Specimen("Older for you", earlier),
            new Specimen("Nothing older", earlier with { Items = [], Next = null }),
            new Specimen("Older, all still new", earlier with { Items = [] }),
            new Specimen("Follow a Topic", new EntryList([new Entry("Follow it to have its new replies come to your door.", [Posts.TopicPageController.FollowForm(topic, false)])], "Your door")),
            new Specimen("Following a Topic", new EntryList([new Entry("Its new replies come to your door.", [Posts.TopicPageController.FollowForm(topic, true)])], "Your door")),
            new Specimen("Quiet a Tangent", new EntryList([new Entry("What is new here counts on your door.", [Tangents.TangentPageController.QuietForm(door + "t/discussions", false)])], "Your door")),
            new Specimen("A quiet Tangent", new EntryList([new Entry("What is new here doesn't count on your door; replies and mentions of you, and the topics you follow, still do.",
                [Tangents.TangentPageController.QuietForm(door + "t/discussions", true)])], "Your door")),
            new Specimen("First steps", FirstStepsOf(door)),
            new Specimen("Nothing to show", new Note("Nothing written here yet.")),
            new Specimen("Topics in a table", new TopicShelf("gallery-topics", "Alive now", rows)),
            new Specimen("An article leading its list", article, Shape.Lead),
            new Specimen("An article in a list", article, Shape.Card),
            new Specimen("A removed article in a list", article with { Id = "gone", Opening = "", Removed = new Posts.TopicPage.Removal(leo, Now.AddHours(-4)) }, Shape.Card),
            new Specimen("A shelf of discussions", new Door.Shelf("discussions", "Discussions", "topic", "Open questions and half-ideas.", door + "t/discussions",
                Tile.For("discussions", "Discussions"), 12, rows.Take(2).ToList())),
            new Specimen("A shelf of articles", new Door.Shelf("articles", "Articles About Everything", "article", "Make room for the next thought.",
                door + "t/articles", Tile.For("articles", "Articles About Everything"), 4, [article, article with { Id = "a2", Title = "A second article" }])),
            new Specimen("A closed Topic’s reply box", new ReplyBox(null, Gallery.Reader, "ada.bsky.social", null, "closed", door + "t/discussions/closed", door,
                door + "t/discussions/media", door + "t/discussions/suggest", Access.Capabilities.Closed.Word, null, new Posts.TopicPage.Closure(leo, Now.AddHours(-2)))),
            new Specimen("A form", seen)
        ])
        {
            Head = new PlaceHead("Kintsugi Architecture", "<p>Repairs that show are the best kind.</p>", leo, 4, 16, false, null),
            Intro = new Intro("<p>Welcome. This is the place’s own welcome, under its head.</p>"),
            Tangents = new TangentList("Tangents", [discussions, articles, workshop], door + "t", door + "new-tangent"),
            Around = new Door.Crowd(4, false, [mara, lumen, leo, you]),
            Nobody = new Door.Crowd(0, false, [])
        });
    }

    /// <summary>First steps as a new place's manager reads them: General here, the rest one press away, their Bluesky line,
    /// banner and picture offered.</summary>
    private static FirstSteps FirstStepsOf(string door)
    {
        Form Press(string action, string name, string value, string words) => Form.Posting(door.TrimEnd('/') + action,
            new Form.Field(name, "", value, Form.Input.Hidden)) with { Tool = "change-appearance", Submit = words, Quiet = true };
        Form Switch(string action, string name, string value, params Form.Choice[] choices) => Form.Posting(door.TrimEnd('/') + action,
            new Form.Field(name, "", value, Form.Input.Switch) { Choices = choices }) with { Tool = "change-appearance", Key = "gallery-" + name };
        return new FirstSteps(
            [
                new("General", "Conversations, open to everyone", door + "t/general", null),
                new("Articles", "Long pieces you publish", null, Press("/t/new", "name", "Articles", "Add")),
                new("The workshop", "Code and experiments", null, Press("/t/new", "name", "The workshop", "Add")),
                new("Off the record", "Private, for whoever you let in", null, Press("/t/new", "name", "Off the record", "Add"))
            ],
            Press("/settings/appearance", "description", "Repairs, systems and the things that outlast them.", "Use it"),
            door + "settings/appearance#space-appearance-description",
            Switch("/settings/appearance", "palette", "gold", new("gold", "Gold") { Swatch = "gold" }, new("jade", "Jade") { Swatch = "jade" },
                new("iris", "Iris") { Swatch = "iris" }, new("copper", "Copper") { Swatch = "copper" }),
            Switch("/settings/appearance", "image", "", new("", "None"), new("the-commons", "The Commons") { Picture = door + "art/the-commons.webp" },
                new("greenhouse", "Greenhouse") { Picture = door + "art/greenhouse.webp" }),
            Press("/settings/appearance", "image", "bluesky", "Use your Bluesky banner"),
            Press("/settings/appearance", "mark", "bluesky", "Use your Bluesky picture"),
            door + "settings/appearance",
            Switch("/settings", "audience", "public", new("public", "Anyone"), new("private", "Only who is in")),
            Switch("/settings/admission", "admission", "join", new("join", "Join at once"), new("apply", "Apply, and you admit them")),
            door + "settings#pointers",
            door + "t/general/new",
            Form.Posting(door + "first-steps/close") with { Tool = "close-first-steps", Submit = "Done", Quiet = true })
        {
            Greeted = "Leo",
            Said = "Repairs, systems and the things that outlast them."
        };
    }
}
