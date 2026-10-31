using System.Security.Cryptography;
using System.Text.Json;
using Koan.Web.OpenGraph;
using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Space;

/// <summary>The door as a resource, the same for a person and an agent: where they are — the place's
/// <see cref="Name"/>, its <see cref="Description"/>, its <see cref="Address"/> and its <see cref="Proprietor"/> — what they <see cref="Can"/> do
/// here, and, for a signed-in reader, <see cref="News"/>: what is new to them since their checkpoints and the sequence
/// the door read through. For everyone, and alone for a visitor or a reader with nothing new, the front page: the
/// <see cref="Tangents"/> the reader may view, the Topics <see cref="Featured"/> and <see cref="Alive"/> in them, and
/// who is <see cref="Around"/>. Every list is bounded, and a count the door could not finish says it is a lower bound
/// with <c>atLeast</c>; nothing is a total over the log. Every link is absolute under the base URL. <see cref="Who"/>
/// is the signed-in reader and <see cref="SignIn"/> where a visitor signs in; <see cref="Day"/> is the UTC day the
/// times are written for. <see cref="Poll"/> is where the reader asks for what happens after this door: the poll past the
/// newest event the door read that the reader may see, or past the newest of its window where the window held none of
/// those, which the stream at the same address also answers. For the place's operator, <see cref="Offers"/> are the
/// open Offers they decide, shown first; for anyone else there are none, and the field is absent. For whoever decides
/// applications, <see cref="Applications"/> says how many wait and where they are decided. <see cref="NewTangent"/> is where a
/// Tangent is created, present only for a reader who may create one. <see cref="Gate"/> is why the reader may not write
/// in the place, absent where they may: for an applicant, that their application is waiting, or was declined, and for a
/// participant timed out, until when (<see cref="Until"/>), which the page says in one sentence.
/// <c>api</c> is the stability promise an agent reads.</summary>
public sealed record Door(
    string Name,
    string Address,
    TangentPage.Author? Proprietor,
    IReadOnlyList<string> Can,
    Door.Since? News,
    IReadOnlyList<Door.Listed> Tangents,
    bool MoreTangents,
    IReadOnlyList<Door.Shown> Featured,
    IReadOnlyList<Door.Shown> Alive,
    Door.Crowd Around,
    string? SignIn,
    Door.Reader? Who,
    DateOnly Day,
    Door.Position Poll,
    string? NewTangent = null)
{
    public int Api => 1;

    /// <summary>What a link to the door previews, where a visitor may read the place, which is where it has
    /// <see cref="Feeds"/>: the place's name, its description and its address, all read for the page.</summary>
    public static SocialCard? Card(Door door)
        => door.Feeds is null ? null : new SocialCard(door.Name, door.Description, Url: door.Address, SiteName: door.Name);

    /// <summary>What the place is, in its proprietor's line; empty until they write one.</summary>
    public string Description { get; init; } = "";

    /// <summary>The proprietor's own HTML that opens the door, sanitized when it was written: present only where they wrote
    /// some.</summary>
    public string? Intro { get; init; }

    /// <summary>The place's articles feeds: present only where a visitor may read the place.</summary>
    public IReadOnlyList<Render.FeedLink>? Feeds { get; init; }

    /// <summary>The open Offers the reader decides: present only for the place's operator, and only while one is open.</summary>
    public IReadOnlyList<Offered>? Offers { get; init; }

    /// <summary>How many applications wait and where they are decided: present only for a reader who holds
    /// <c>ManageStanding</c> in the place, and only while one waits.</summary>
    public Waiting? Applications { get; init; }

    /// <summary>Where every Tangent the reader may view is listed, present where more follow those the door lists.</summary>
    public string? AllTangents { get; init; }

    /// <summary>Why the reader may not write in the place — <c>sign-in</c>, <c>apply</c>, <c>declined</c>,
    /// <c>timed-out</c>, or another gate — absent where they may.</summary>
    public string? Gate { get; init; }

    /// <summary>When the reader's timeout ends, while they are timed out; absent otherwise.</summary>
    public DateTimeOffset? Until { get; init; }

    /// <summary>What is new to a signed-in reader: the place checkpoint the door read past (<see cref="From"/>, zero
    /// when they have none) and the sequence it read through (<see cref="Through"/>); first the Topics and Tangents they
    /// watch that hold something new, then replies to them, mentions of them, what is new in each Tangent and the new
    /// articles; <see cref="Taken"/> holds the Topics taken on a tangent from their Posts, which are not replies.
    /// <see cref="CaughtUp"/> is the form that moves the place checkpoint to <see cref="Through"/>, present while there
    /// is news. <see cref="Admitted"/> is who admitted the reader and when, and <see cref="Lifted"/> who lifted their
    /// timeout or ban and when, where that happened since their checkpoint and they stand admitted still;
    /// <see cref="Roles"/> are the roles someone gave them or took from them since then, the newest change of each;
    /// <see cref="Edited"/> holds their Posts someone else edited since then and still stand, each once, and
    /// <see cref="Removed"/> those someone else removed, each with who did it.
    /// <see cref="Notifications"/> is how many of the reader's notifications they have not seen and where they are,
    /// present while there are some. <see cref="First"/> says this is their first door: they have no place checkpoint, so
    /// nothing in the place counts as new to them and only what concerns them is here.</summary>
    public sealed record Since(
        long From,
        long Through,
        IReadOnlyList<Watched> Watched,
        Group Replies,
        Group Mentions,
        IReadOnlyList<InTangent> PerTangent,
        Group Articles,
        Form? CaughtUp,
        Group? Taken = null,
        Decision? Admitted = null,
        Decision? Lifted = null,
        IReadOnlyList<RoleChange>? Roles = null,
        Group? Edited = null,
        Group? Removed = null)
    {
        /// <summary>How many of the reader's notifications they have not seen and where they are, present while there are
        /// some.</summary>
        public Events.Notifications.Unseen? Notifications { get; init; }

        /// <summary>Whether the reader has never been at the door.</summary>
        public bool First { get; init; }

        /// <summary>Whether nothing is new to the reader.</summary>
        public bool Quiet => Replies.Count == 0 && Mentions.Count == 0 && PerTangent.Count == 0 && Articles.Count == 0 && (Taken?.Count ?? 0) == 0
                             && Admitted is null && Lifted is null && (Roles?.Count ?? 0) == 0 && (Edited?.Count ?? 0) == 0 && (Removed?.Count ?? 0) == 0;
    }

    /// <summary>A decision about the reader's standing: who made it, and when; for a lift, the standing it lifted
    /// (<c>timed-out</c> or <c>banned</c>) in <see cref="Was"/>.</summary>
    public sealed record Decision(TangentPage.Author Who, DateTimeOffset At, string? Was = null);

    /// <summary>A role someone gave the reader or took from them: who, when, the role (<c>moderator</c> or
    /// <c>administrator</c>) and whether it was given.</summary>
    public sealed record RoleChange(TangentPage.Author Who, DateTimeOffset At, string Role, bool Given);

    /// <summary>How many of a kind are new, whether that is a lower bound because the window the door read was full,
    /// and the newest of them, a few at most.</summary>
    public sealed record Group(int Count, bool AtLeast, IReadOnlyList<NewPost> Posts);

    /// <summary>One new Post: who wrote it, or who edited or removed it, the Topic and Tangent it is in, its address, its
    /// first words, when, and who other than its author last edited it, where someone did.</summary>
    public sealed record NewPost(string PostId, TangentPage.Author Who, string Topic, string Tangent, string Address, string Line, DateTimeOffset At,
        TangentPage.Edited? Edited = null);

    /// <summary>A Topic or a Tangent the reader watches that holds new Posts: its scope (<c>topic</c> or <c>tangent</c>),
    /// its name, the Tangent it is in (itself, for a Tangent), its address, how many of its Posts are new, and whether
    /// that is a lower bound.</summary>
    public sealed record Watched(string Scope, string Name, string Tangent, string Address, int New, bool AtLeast);

    /// <summary>What is new in one Tangent: its Topics and its replies.</summary>
    public sealed record InTangent(string Slug, string Name, string Address, int Topics, int Replies, bool AtLeast);

    /// <summary>One open Offer as its decider reads it: the Offer's Id, what it would do (<c>address</c>: use
    /// <see cref="Subject"/>, an origin, as the place's address), and the evidence Koan holds since this process started —
    /// how many requests arrived on it and since when, absent after a restart until it is seen again; then its two answers,
    /// each a form carrying the Id in its <c>offer</c> field.</summary>
    public sealed record Offered(string Offer, string Kind, string Subject, long? Count, DateTimeOffset? Since, Form Use, Form Decline);

    /// <summary>How many applications wait, whether that is a lower bound because the read was full, and the address of
    /// the applications, where each is admitted or declined.</summary>
    public sealed record Waiting(int Count, bool AtLeast, string Address);

    /// <summary>A Tangent the reader may view: its slug, name, kind, address, how many Topics were started there, and
    /// how many of its Posts are new to the reader.</summary>
    public sealed record Listed(string Slug, string Name, string Kind, string Address, int TopicCount, int New);

    /// <summary>A Topic on the front page and the Tangent it is in.</summary>
    public sealed record Shown(string Tangent, string TangentName, TangentPage.Topic Topic);

    /// <summary>Who is around: how many wrote within the window the door read, whether that is a lower bound, and the
    /// most recent of them.</summary>
    public sealed record Crowd(int Count, bool AtLeast, IReadOnlyList<TangentPage.Author> People);

    /// <summary>The signed-in reader: their id, the name to show, their handle, the highest role they hold where they
    /// hold one, their avatar, where they sign out, coming back to the door, and the place's settings, for one who holds
    /// <c>ManageSpace</c>, <c>ManageStanding</c> or <c>ManageRoles</c>, and how many of their notifications they have not
    /// seen.</summary>
    public sealed record Reader(string Id, string Name, string Handle, string? Role, string? Avatar, string SignOut, string? Settings = null,
        Events.Notifications.Unseen? Notifications = null);

    /// <summary>Where the door stands in the log and where to ask again: the sequence it read through for this reader, and the
    /// poll's address past it.</summary>
    public sealed record Position(long Through, string Next);

    /// <summary>The version the door's <c>ETag</c> carries: a hash of everything the door says, so a new event the
    /// reader may see, a moved checkpoint, a changed <c>can</c> or a new day changes it. A door with the form carries a
    /// new antiforgery token every time, so its version never repeats and it is never answered as unchanged.</summary>
    public static long Version(Door door)
        => BitConverter.ToInt64(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(door)), 0) & long.MaxValue;
}
