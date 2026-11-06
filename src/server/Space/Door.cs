using System.Security.Cryptography;
using System.Text.Json;
using Koan.Web.OpenGraph;
using Tangent.Render;

namespace Tangent.Space;

/// <summary>The door as a resource, the same for a person and an agent: where they are — the place's
/// <see cref="Name"/>, its <see cref="Description"/>, its <see cref="Address"/> and its <see cref="Proprietor"/> — what they <see cref="Can"/> do
/// here, and, for a signed-in reader, <see cref="News"/>: what is new to them since they were here and the sequence
/// the door read through. For everyone, and alone for a visitor or a reader with nothing new, the front page: the
/// <see cref="Tangents"/> the reader may view, the Topics <see cref="Featured"/> and <see cref="Alive"/> in them, a
/// shelf of each Tangent's latest (<see cref="Shelves"/>), and who is <see cref="Around"/>. Every list is bounded, and a count the door could not finish says it is a lower bound
/// with <c>atLeast</c>; nothing is a total over the log. Every link is absolute under the base URL. <see cref="Who"/>
/// is the signed-in reader and <see cref="SignIn"/> where a visitor signs in; <see cref="Day"/> is the UTC day the
/// times are written for. <see cref="Poll"/> is where the reader asks for what happens after this door: the poll past the
/// newest event the door read that the reader may see, or past the newest of its window where the window held none of
/// those, which the stream at the same address also answers. For the place's operator, <see cref="Offer"/> is the open
/// Offer of an address, shown first; for anyone else there is none, and the field is absent. For whoever decides
/// applications, <see cref="Applications"/> says how many wait and where they are decided. <see cref="NewTangent"/> is where a
/// Tangent is created, present only for a reader who may create one. <see cref="Gate"/> is why the reader may not write
/// in the place, absent where they may: for an applicant, that their application is waiting, or was declined, and for a
/// participant timed out, until when (<see cref="Until"/>), which the page says in one sentence.
/// <c>api</c> is the stability promise an agent reads.</summary>
[Page(Layout.Home, Refreshes = true)]
public sealed record Door(
    string Name,
    string Address,
    Person? Proprietor,
    IReadOnlyList<string> Can,
    IReadOnlyList<TangentEntry> Tangents,
    bool MoreTangents,
    IReadOnlyList<Door.Shown> Featured,
    IReadOnlyList<Door.Shown> Alive,
    string? SignIn,
    Door.Reader? Who,
    DateOnly Day,
    Door.Position Poll,
    string? NewTangent = null)
{
    public int Api => 1;

    /// <summary>The frame the shell draws around the door: the place, which the masthead names and nothing repeats, no path
    /// below it, and who reads, or where a visitor signs in.</summary>
    public static Frame Frame(Door door)
    {
        var who = door.Who is { } reader ? new Who(reader.Id, reader.Name, reader.Handle, reader.Role, reader.Avatar, reader.SignOut, reader.Settings, reader.ForYou) : null;
        return new(door.Name, door.Address, [], who, door.SignIn, door.Feeds);
    }

    /// <summary>What a link to the door previews, where a visitor may read the place, which is where it has
    /// <see cref="Feeds"/>: the place's name, its description and its address, all read for the page.</summary>
    public static SocialCard? Card(Door door)
        => door.Feeds is null ? null : new SocialCard(door.Name, Space.PlaceWords.Plain(Space.PlaceWords.DescriptionPart, door.Description), Url: door.Address,
            SiteName: door.Name, Image: Koan.Web.OpenGraph.CardImage.Url(door.Image));

    /// <summary>What the place is, in its proprietor's line of Markdown; empty until they write one.</summary>
    public string Description { get; init; } = "";

    /// <summary>The description as the door draws it, its Markdown rendered as a Post's body is; absent while it is empty.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? DescriptionHtml { get; init; }

    /// <summary>The place's welcome under the door's head, its Markdown rendered as a Post's body is; present only where the
    /// place wrote one.</summary>
    public string? Intro { get; init; }

    /// <summary>The address of the place's image, behind the door's head; present only where the place wears one.</summary>
    public string? Image { get; init; }

    /// <summary>The place's articles feeds: present only where a visitor may read the place.</summary>
    public IReadOnlyList<Render.FeedLink>? Feeds { get; init; }

    /// <summary>Where every Tangent the reader may view is listed, present where more follow those the door lists.</summary>
    public string? AllTangents { get; init; }

    /// <summary>Why the reader may not write in the place — <c>sign-in</c>, <c>apply</c>, <c>declined</c>,
    /// <c>timed-out</c>, or another gate — absent where they may.</summary>
    public string? Gate { get; init; }

    /// <summary>When the reader's timeout ends, while they are timed out; absent otherwise.</summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>The place's head: its description, whose place it is, what it holds and its image.</summary>
    [Slot(Slot.Head), Newtonsoft.Json.JsonIgnore]
    public PlaceHead Head => new(Name, DescriptionHtml, Proprietor, Tangents.Count, Tangents.Sum(tangent => tangent.TopicCount), MoreTangents, Image);

    [Slot(Slot.Head), Newtonsoft.Json.JsonIgnore]
    public Intro? Greeting => Intro is { } intro ? new Intro(intro) : null;

    /// <summary>The Tangents the reader may view, in the rail.</summary>
    [Slot(Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public TangentList Listed => new("Tangents", Tangents, AllTangents, NewTangent);

    /// <summary>Who is around: how many wrote within the window the door read, whether that is a lower bound, and the most
    /// recent of them.</summary>
    [Slot(Slot.Rail)]
    public required Crowd Around { get; init; }

    /// <summary>Why the reader may not write, in one line, where their standing is why.</summary>
    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public StandingLine? Standing => Gate is { } gate && (gate == Access.Capabilities.Apply.Word || gate == Access.Capabilities.Declined.Word
        || (gate == Access.Capabilities.TimedOut.Word && Until is not null)) ? new StandingLine(gate, Until) : null;

    /// <summary>The open Offer the reader decides: present only for the place's operator, and only while one is open.</summary>
    [Slot(Slot.Column)]
    public Offered? Offer { get; init; }

    /// <summary>How many applications wait and where they are decided: present only for a reader who holds
    /// <c>ManageStanding</c> in the place, and only while one waits.</summary>
    [Slot(Slot.Column)]
    public Waiting? Applications { get; init; }

    /// <summary>The asks to join that wait in the members-only Tangents the reader manages, by Tangent, each with where it
    /// is decided: present only while one waits.</summary>
    [Slot(Slot.Column)]
    public IReadOnlyList<Asking>? Asks { get; init; }

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public ContinueReading? Continue => Who is { } reader ? new ContinueReading(reader.Id) : null;

    /// <summary>What is new to a signed-in reader; see <see cref="Since"/>.</summary>
    public Since? News { get; init; }

    /// <summary>The news as the door draws it: with nothing new, only where no welcome stands in its place.</summary>
    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Since? Told => News is { Quiet: true } && Welcoming is not null ? null : News;

    /// <summary>The few steps that make the place its manager's own: present only for a reader who holds
    /// <c>ManageSpace</c>, and only while nothing has been written in any Tangent but the moderators'.</summary>
    [Slot(Slot.Column)]
    public Welcome? Welcoming { get; init; }

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Note? Nothing => Welcoming is null && Featured.Count == 0 && Alive.Count == 0 ? new Note("Nothing written here yet.") : null;

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public TopicShelf? FeaturedShelf => Featured.Count == 0 ? null : new TopicShelf("door-featured", "Featured", Featured.Select(shown => shown.Topic).ToList());

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public TopicShelf? AliveShelf => Alive.Count == 0 ? null : new TopicShelf("door-alive", "Alive now", Alive.Select(shown => shown.Topic).ToList());

    /// <summary>For a visitor and a reader with nothing new, a shelf for each of the first Tangents the reader may view
    /// that holds a Topic the front page does not show above, the moderators' Tangent aside; absent otherwise.</summary>
    [Slot(Slot.Column)]
    public IReadOnlyList<Shelf>? Shelves { get; init; }

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public LiveUpdates Live => new(LiveUpdates.Door);

    /// <summary>What is new to a signed-in reader (<see cref="Events.Unread"/>): where since they were here begins in the
    /// log (<see cref="From"/>, the later of their arrival and their place mark) and the sequence the door read through
    /// (<see cref="Through"/>). What is for them: <see cref="Replies"/> to them, <see cref="Mentions"/> of them,
    /// <see cref="Taken"/>, the Topics taken on a tangent from their Posts, which are not replies, and, for whoever may
    /// view the moderators' Tangent, <see cref="Reports"/>; <see cref="Admitted"/>, who admitted them and when, and
    /// <see cref="Lifted"/>, who lifted their timeout or ban and when, <see cref="TimedOut"/>, who timed them out, until
    /// when, and <see cref="Declined"/>, who declined their application, where that is the newest change of their
    /// standing since; <see cref="Applications"/>, for whoever decides them, how many applications arrived since;
    /// <see cref="Roles"/>, the roles someone gave them or took from them since, the newest change of each;
    /// <see cref="Edited"/>, their Posts someone else edited since that still stand, each once, and
    /// <see cref="Removed"/>, those someone else removed, each with who did it. Then what is new in each Tangent
    /// (<see cref="PerTangent"/>) and the new <see cref="Articles"/>, past those for the reader. A Post's entry stays while the Post is new, so
    /// reading its Topic clears it here. <see cref="Seen"/> is the form that says all of it is seen, through
    /// <see cref="Through"/>, present while there is news.</summary>
    public sealed record Since(
        long From,
        long Through,
        Group Replies,
        Group Mentions,
        IReadOnlyList<InTangent> PerTangent,
        Group Articles,
        Form? Seen,
        Group? Taken = null,
        Decision? Admitted = null,
        Decision? Lifted = null,
        IReadOnlyList<RoleChange>? Roles = null,
        Group? Edited = null,
        Group? Removed = null,
        Group? Reports = null,
        Decision? TimedOut = null,
        Decision? Declined = null,
        Waiting? Applications = null,
        IReadOnlyList<Let>? Let = null) : IPart
    {
        /// <summary>Whether nothing is new to the reader, so nothing is counted for them either.</summary>
        public bool Quiet => Replies.Count == 0 && Mentions.Count == 0 && PerTangent.Count == 0 && Articles.Count == 0 && (Taken?.Count ?? 0) == 0
                             && Admitted is null && Lifted is null && TimedOut is null && Declined is null && (Roles?.Count ?? 0) == 0
                             && (Edited?.Count ?? 0) == 0 && (Removed?.Count ?? 0) == 0 && (Reports?.Count ?? 0) == 0 && Applications is null
                             && (Let?.Count ?? 0) == 0;
    }

    /// <summary>A decision about the reader's ask to join a members-only Tangent, or their being let in: who, when, the
    /// Tangent's name and address, and whether they were let in.</summary>
    public sealed record Let(Person Who, DateTimeOffset At, string Tangent, string Address, bool Given);

    /// <summary>The asks to join that wait in one Tangent the reader manages: its name, how many, whether that is a lower
    /// bound, and its people, where each is decided.</summary>
    public sealed record Asking(string Tangent, int Count, bool AtLeast, string Address) : IPart;

    /// <summary>A decision about the reader's standing: who made it, and when; for a lift, the standing it lifted
    /// (<c>timed-out</c> or <c>banned</c>) in <see cref="Was"/>; for a timeout, when it ends, or ended, in
    /// <see cref="Until"/>.</summary>
    public sealed record Decision(Person Who, DateTimeOffset At, string? Was = null, DateTimeOffset? Until = null);

    /// <summary>A role someone gave the reader or took from them: who, when, the role by its word (<c>moderator</c>),
    /// whether it was given, and the role's name now, its word where the role is gone.</summary>
    public sealed record RoleChange(Person Who, DateTimeOffset At, string Role, bool Given, string Name);

    /// <summary>How many of a kind are new, whether that is a lower bound because a read the door made was full, and
    /// the newest of them, a few at most.</summary>
    public sealed record Group(int Count, bool AtLeast, IReadOnlyList<NewPost> Posts);

    /// <summary>One new Post: who wrote it, reported with it, or edited or removed it, the Topic and Tangent it is in, its
    /// address, its first words, when, and who other than its author last edited it, where someone did.</summary>
    public sealed record NewPost(string PostId, Person Who, string Topic, string Tangent, string Address, string Line, DateTimeOffset At,
        Tangents.TangentPage.Edited? Edited = null) : IPart
    {
        /// <summary>What someone did to the reader's own Post, where the row says it in place of who wrote it.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Did { get; init; }
    }

    /// <summary>What is new in one Tangent of its kind: its Topics and its replies, and how many of them no other part of
    /// the news counts (<see cref="Others"/>) — neither for the reader nor a new article — so a sentence that adds the
    /// parts counts each new Post once.</summary>
    public sealed record InTangent(string Slug, string Name, string Kind, string Address, int Topics, int Replies, int Others, bool AtLeast);

    /// <summary>The open Offer as its decider reads it: what it would do (<c>address</c>: use <see cref="Subject"/>, an
    /// origin, as the place's address), and the evidence Koan holds since this process started — how many requests arrived
    /// on it and since when; then its two answers, each a form carrying the origin in its <c>origin</c> field.</summary>
    public sealed record Offered(string Kind, string Subject, long Count, DateTimeOffset Since, Form Use, Form Decline) : IPart;

    /// <summary>How many applications wait, whether that is a lower bound because the read was full, and the address of
    /// the applications, where each is admitted or declined.</summary>
    public sealed record Waiting(int Count, bool AtLeast, string Address) : IPart;

    /// <summary>Where the place's manager makes it theirs: the first Topic, in its first Tangent where it has one; the
    /// line that says what the place is, at its field (<see cref="Describe"/>); who comes in; and bringing their agent.</summary>
    public sealed record Welcome(string? FirstTopic, string Describe, string Admission, string BringYourAgent) : IPart
    {
        /// <summary>The place's name, which the welcome says.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string Place { get; init; } = "";
    }

    /// <summary>One Tangent's shelf: its slug, name, kind, line, address, tile, how many Topics were started there, and its
    /// latest few, newest activity first, leaving out those the front page shows above it.</summary>
    public sealed record Shelf(string Slug, string Name, string Kind, string Description, string Address, Render.Tile Tile, int TopicCount,
        IReadOnlyList<TopicEntry> Topics) : IPart;

    /// <summary>A Topic on the front page and the Tangent it is in.</summary>
    public sealed record Shown(string Tangent, string TangentName, TopicEntry Topic);

    /// <summary>Who is around: how many wrote within the window the door read, whether that is a lower bound, and the
    /// most recent of them.</summary>
    public sealed record Crowd(int Count, bool AtLeast, IReadOnlyList<Person> People) : IPart;

    /// <summary>The signed-in reader: their id, the name to show, their handle, the highest role they hold where they
    /// hold one, their avatar, where they sign out, coming back to the door, and the place's settings, for one who holds
    /// <c>ManageSpace</c>, <c>ManageStanding</c> or <c>ManageRoles</c>, and how many things are for them and new, which
    /// this door lists.</summary>
    public sealed record Reader(string Id, string Name, string Handle, string? Role, string? Avatar, string SignOut, string? Settings = null,
        Events.Unread.Badge? ForYou = null);

    /// <summary>Where the door stands in the log and where to ask again: the sequence it read through for this reader, and the
    /// poll's address past it.</summary>
    public sealed record Position(long Through, string Next);

    /// <summary>The version the door's <c>ETag</c> carries: a hash of everything the door says, so a new event the
    /// reader may see, a moved mark, a changed <c>can</c> or a new day changes it.</summary>
    public static long Version(Door door)
        => Render.PageVersion.Of(door);
}
