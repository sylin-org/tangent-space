using System.Collections.Concurrent;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Tangent.Posts;

namespace Tangent.Connections;

/// <summary>An inbound reply or quote from elsewhere, attributed: its kind, who wrote it — their DID, handle and name
/// where they have one — its words, when, and where it is read. Nothing of it is a Post of the place.</summary>
public sealed record Reference(string Kind, string Did, string Handle, string? Name, string Text, DateTimeOffset At, string Address)
{
    public const string Reply = "reply";
    public const string Quote = "quote";
}

/// <summary>What the network says about one published article, keyed by its Post's Id: the likes, reposts, quotes and
/// replies of the Bluesky post its author's Connection wrote, and the newest replies and quotes as references. It is a
/// Koan job: started once the article goes out (<see cref="Syndication"/>), each run reads the public AppView
/// (<see cref="AppView"/>) outside any hold and keeps what it found, then submits the next run — soon while someone has read the
/// article lately (<see cref="Readers"/>, which a page read tells in memory and nothing else), later and later while no
/// one has, at most <see cref="LongestWait"/>; once the article's copy is retracted, or the article is deleted, it forgets
/// what it heard and stops.
/// A failure keeps what it had and tries at the same pace.</summary>
[JobIdempotent(nameof(Id))]
public sealed class Elsewhere : Entity<Elsewhere>, IKoanJob<Elsewhere>
{
    /// <summary>The wait between reads while someone is reading.</summary>
    public static readonly TimeSpan Reading = TimeSpan.FromMinutes(2);

    /// <summary>How long a read of the page counts as someone reading.</summary>
    public static readonly TimeSpan Lately = TimeSpan.FromMinutes(15);

    /// <summary>The longest wait between reads.</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromHours(1);

    /// <summary>The article's author, whose Connection published it.</summary>
    public string AuthorId { get; set; } = "";

    public int Likes { get; set; }
    public int Reposts { get; set; }
    public int Quotes { get; set; }
    public int Replies { get; set; }
    public List<Reference> References { get; set; } = [];

    /// <summary>How many runs in a row found no one reading.</summary>
    public int Quiet { get; set; }

    /// <summary>Starts the job of a published article, keeping what it already heard.</summary>
    public static async Task Start(Post article, CancellationToken ct)
        => await (await Get(article.Id, ct) ?? new Elsewhere { Id = article.Id, AuthorId = article.AuthorId }).Job.Submit(ct: ct);

    public static async Task Execute(Elsewhere elsewhere, JobContext context, CancellationToken ct)
    {
        var receipt = await Receipt.Get(Receipt.Key(elsewhere.AuthorId, elsewhere.Id), ct);
        if (receipt is not { Note: { } note } || !receipt.IsLive() || await Post.Get(elsewhere.Id, ct) is null)
        {
            elsewhere.Keep(Heard.Nothing);
            return;
        }

        try
        {
            elsewhere.Keep(await context.Services.GetRequiredService<AppView>().Read(note, ct));
        }
        catch (Exception failure) when (failure is HttpRequestException or System.Text.Json.JsonException
                                        or OperationCanceledException && !ct.IsCancellationRequested)
        {
            context.Logger.LogWarning(failure, "What the network says about {Article} was not read", elsewhere.Id);
        }

        var reading = context.Services.GetRequiredService<Readers>().Lately(elsewhere.Id);
        elsewhere.Quiet = reading ? 0 : Math.Min(elsewhere.Quiet + 1, 10);
        // The next read is a job of its own: a run that defers itself keeps nothing it changed.
        await elsewhere.Job.Submit("", TimeSpan.FromSeconds(Math.Min(LongestWait.TotalSeconds, Reading.TotalSeconds * Math.Pow(2, elsewhere.Quiet))), ct);
    }

    private void Keep(Heard heard)
    {
        (Likes, Reposts, Quotes, Replies) = (heard.Likes, heard.Reposts, heard.Quotes, heard.Replies);
        References = [.. heard.References];
    }

    /// <summary>What the page of a published article shows of it, from one keyed read, telling <paramref name="readers"/>
    /// someone read it; nothing until the network said something. Its Bluesky post is under the article's own key.</summary>
    public static async Task<Line?> For(Post root, Readers readers, CancellationToken ct)
    {
        readers.Read(root.Id);
        var elsewhere = await Get(root.Id, ct);
        return elsewhere is null || elsewhere.Likes + elsewhere.Reposts + elsewhere.Quotes + elsewhere.Replies + elsewhere.References.Count == 0
            ? null
            : new Line(elsewhere.Likes, elsewhere.Reposts, elsewhere.Quotes, elsewhere.Replies, Publishing.Bluesky($"at://{root.AuthorDid}/{AtprotoAdapter.Notes}/{root.Id}") ?? "", elsewhere.References);
    }

    /// <summary>What an article's page shows from elsewhere: the counts of its Bluesky post, where it is read, and the
    /// newest replies and quotes there.</summary>
    public sealed record Line(int Likes, int Reposts, int Quotes, int Replies, string Address, IReadOnlyList<Reference> References) : Render.IPart;
}

/// <summary>When each published article was last read, in memory and nowhere else, so the <see cref="Elsewhere"/> job
/// reads the network often only while someone is reading. A page read sets one entry and writes nothing; it holds at
/// most <see cref="Most"/> articles, and a restart forgets them all, which only slows the next read.</summary>
public sealed class Readers
{
    public const int Most = 10_000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _read = new(StringComparer.Ordinal);

    public void Read(string articleId)
    {
        if (_read.Count >= Most && !_read.ContainsKey(articleId) && !Forgot()) return;
        _read[articleId] = DateTimeOffset.UtcNow;
    }

    public bool Lately(string articleId)
        => _read.TryGetValue(articleId, out var at) && DateTimeOffset.UtcNow - at < Elsewhere.Lately;

    /// <summary>Forgets the reads that no longer count, and says whether that made room.</summary>
    private bool Forgot()
    {
        foreach (var (id, at) in _read)
            if (DateTimeOffset.UtcNow - at >= Elsewhere.Lately) _read.TryRemove(id, out _);
        return _read.Count < Most;
    }
}
