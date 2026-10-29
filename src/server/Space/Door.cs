using System.Security.Cryptography;
using System.Text.Json;
using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Space;

/// <summary>The door as a resource, the same for a person and an agent: where they are — the place's
/// <see cref="Name"/>, its <see cref="Address"/> and its <see cref="Proprietor"/> — what they <see cref="Can"/> do
/// here, and, for a signed-in reader, <see cref="News"/>: what is new to them since their checkpoints and the sequence
/// the door read through. For everyone, and alone for a visitor or a reader with nothing new, the front page: the
/// <see cref="Tangents"/> the reader may view, the Topics <see cref="Featured"/> and <see cref="Alive"/> in them, and
/// who is <see cref="Around"/>. Every list is bounded, and a count the door could not finish says it is a lower bound
/// with <c>atLeast</c>; nothing is a total over the log. Every link is absolute under the base URL. <see cref="Who"/>
/// is the signed-in reader and <see cref="SignIn"/> where a visitor signs in; <see cref="Day"/> is the UTC day the
/// times are written for. <see cref="Poll"/> is where the reader asks for what happens after this door: the poll past the
/// newest event the door read that the reader may see, which the stream at the same address also answers.
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
    Door.Position Poll)
{
    public int Api => 1;

    /// <summary>What is new to a signed-in reader: the place checkpoint the door read past (<see cref="From"/>, zero
    /// when they have none) and the sequence it read through (<see cref="Through"/>); first the Topics and Tangents they
    /// watch that hold something new, then replies to them, mentions of them, what is new in each Tangent and the new
    /// articles. <see cref="CaughtUp"/> is the form that moves the place
    /// checkpoint to <see cref="Through"/>, present while there is news.</summary>
    public sealed record Since(
        long From,
        long Through,
        IReadOnlyList<Watched> Watched,
        Group Replies,
        Group Mentions,
        IReadOnlyList<InTangent> PerTangent,
        Group Articles,
        CaughtUp? CaughtUp)
    {
        /// <summary>Whether nothing is new to the reader.</summary>
        public bool Quiet => Replies.Count == 0 && Mentions.Count == 0 && PerTangent.Count == 0 && Articles.Count == 0;
    }

    /// <summary>How many of a kind are new, whether that is a lower bound because the window the door read was full,
    /// and the newest of them, a few at most.</summary>
    public sealed record Group(int Count, bool AtLeast, IReadOnlyList<NewPost> Posts);

    /// <summary>One new Post: who wrote it, the Topic and Tangent it is in, its address, its first words and when.</summary>
    public sealed record NewPost(string PostId, TangentPage.Author Who, string Topic, string Tangent, string Address, string Line, DateTimeOffset At);

    /// <summary>A Topic or a Tangent the reader watches that holds new Posts: its scope (<c>topic</c> or <c>tangent</c>),
    /// its name, the Tangent it is in (itself, for a Tangent), its address, how many of its Posts are new, and whether
    /// that is a lower bound.</summary>
    public sealed record Watched(string Scope, string Name, string Tangent, string Address, int New, bool AtLeast);

    /// <summary>What is new in one Tangent: its Topics and its replies.</summary>
    public sealed record InTangent(string Slug, string Name, string Address, int Topics, int Replies, bool AtLeast);

    /// <summary>The form that marks the door seen: where it posts, the sequence it carries in the <c>through</c> field,
    /// and its antiforgery token.</summary>
    public sealed record CaughtUp(string Action, long Through, FormToken Token);

    /// <summary>A Tangent the reader may view: its slug, name, kind, address, how many Topics were started there, and
    /// how many of its Posts are new to the reader.</summary>
    public sealed record Listed(string Slug, string Name, string Kind, string Address, int TopicCount, int New);

    /// <summary>A Topic on the front page and the Tangent it is in.</summary>
    public sealed record Shown(string Tangent, string TangentName, TangentPage.Topic Topic);

    /// <summary>Who is around: how many wrote within the window the door read, whether that is a lower bound, and the
    /// most recent of them.</summary>
    public sealed record Crowd(int Count, bool AtLeast, IReadOnlyList<TangentPage.Author> People);

    /// <summary>The signed-in reader: their id, the name to show, their handle, the highest role they hold where they
    /// hold one, their avatar, and where they sign out, coming back to the door.</summary>
    public sealed record Reader(string Id, string Name, string Handle, string? Role, string? Avatar, string SignOut);

    /// <summary>Where the door stands in the log and where to ask again: the sequence it read through for this reader, and the
    /// poll's address past it.</summary>
    public sealed record Position(long Through, string Next);

    /// <summary>The version the door's <c>ETag</c> carries: a hash of everything the door says, so a new event the
    /// reader may see, a moved checkpoint, a changed <c>can</c> or a new day changes it. A door with the form carries a
    /// new antiforgery token every time, so its version never repeats and it is never answered as unchanged.</summary>
    public static long Version(Door door)
        => BitConverter.ToInt64(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(door)), 0) & long.MaxValue;
}
