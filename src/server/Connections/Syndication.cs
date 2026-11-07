using System.Net;
using Koan.Data.Abstractions;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Sorting;
using Koan.Jobs;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;

namespace Tangent.Connections;

/// <summary>A Connection's work, as its job runs it: the log past its cursor, a bounded page; the articles those rows
/// name and every article its receipts name; and for each of its participant's articles, the version and the Tangent its
/// receipt delivered against the Post's own (<see cref="Receipt.Behind"/>). A public article that is ahead of its receipt, or
/// moved since, is published or updated — one that arrived after the Connection began publishing on its own, or one they
/// chose — and one that is removed or private, or that its author took down (<see cref="Receipt.Withheld"/>), is retracted,
/// as is the copy of one deleted, from its receipt alone. Only the
/// author's own words go out as theirs: a version someone else edited waits until they choose it (<see cref="Receipt.Theirs"/>).
/// An article that goes out starts hearing what the network says about it (<see cref="Elsewhere"/>).
/// An article written as an account the participant is no longer bound to is only ever taken down, from the repository
/// its receipt names, through that account's grant where it still holds (<see cref="TakeDown"/>). The proprietor's
/// Connection first puts the place as their publication where it is missing or has changed, and a Connection withdraws
/// the publication it put, from the account it is under, once the place is private or someone else is its proprietor. Every
/// write is a put at a fixed key, so a run repeated after a failure lands once. The network is read and written outside
/// any hold, and each receipt is sent as a command. A failure stops the run, leaves the cursor where it was, says what
/// it could not do on the Connection and tries again later, sooner at first; a refusal that needs the grant again waits
/// for it.</summary>
public static class Syndication
{
    /// <summary>The most rows of the log one run reads; a full page runs again at once for the rest.</summary>
    public const int Page = 200;

    /// <summary>The most receipts one run compares, the newest first.</summary>
    public const int ReceiptLimit = 500;

    /// <summary>The longest a failing Connection waits before trying again.</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromHours(1);

    public const string Unreachable = "Couldn't reach Bluesky, trying again.";
    public const string Ungranted = "Bluesky no longer lets this place publish as you.";
    public const string Refused = "Bluesky refused an article, trying again.";
    public const string NoAddress = "This place has no address of its own yet; it publishes once it has one.";

    public static async Task Run(Connection connection, JobContext context, CancellationToken ct)
    {
        if (!connection.Publishes()) return;
        var adapter = context.Services.GetRequiredService<AtprotoAdapter>();
        if (adapter.Address is null)
        {
            Troubled(connection, context, NoAddress);
            return;
        }

        var rows = await Event.Query(e => e.Seq > connection.Cursor,
            new QueryDefinition { Page = 1, PageSize = Page }.WithSort<Event>(sort => sort.OrderBy(e => e.Seq)), ct);
        var through = rows.Count > 0 ? rows[^1].Seq : connection.Cursor;
        var did = await Participants.Participant.DidOf(connection.Id, ct);
        var receipts = (await Receipt.Query(receipt => receipt.ConnectionId == connection.Id,
                new QueryDefinition { Page = 1, PageSize = ReceiptLimit }.WithSort<Receipt>(sort => sort.OrderByDescending(receipt => receipt.At)), ct))
            .ToDictionary(receipt => receipt.PostId, StringComparer.Ordinal);
        var named = rows.Where(row => row.Kind is EventKind.PostCreated or EventKind.PostEdited or EventKind.PostRemoved or EventKind.PostMoved
                                          or EventKind.RemovalUndone
                                      && row.PostId is not null && row.PostId == row.TopicId)
            .Select(row => row.PostId!);
        var ids = named.Concat(receipts.Keys).Distinct(StringComparer.Ordinal).ToList();
        var found = ids.Count == 0 ? [] : (await Post.Get(ids, ct)).OfType<Post>().ToList();
        var articles = found.Where(post => post.ParentId is null && post.Kind == PostKind.Article && post.AuthorId == connection.Id).ToList();
        var standing = found.Select(post => post.Id).ToHashSet(StringComparer.Ordinal);
        var gone = receipts.Values.Where(receipt => !standing.Contains(receipt.PostId)).ToList();

        try
        {
            var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
            var publication = await Publication.Get(Publication.SingleRow, ct);
            if (publication is not null && publication.Proprietor == connection.Id
                                        && (space.Audience != Audience.Public || space.ProprietorId != connection.Id))
            {
                await Withdrawn(publication, did, adapter, context, ct);
                publication = null;
            }

            if (space.ProprietorId == connection.Id && space.Audience == Audience.Public)
                publication = await Placed(space, did, publication, adapter, context, ct);

            var audiences = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var article in articles)
            {
                if (!audiences.TryGetValue(article.TangentId, out var open))
                    audiences[article.TangentId] = open = await Publishing.Open(space, article.TangentId, ct);
                var receipt = receipts.GetValueOrDefault(article.Id);
                if (open && !article.Removed && receipt?.Withheld != true)
                {
                    if (article.AuthorDid == did) await Deliver(connection, article, receipt, publication, adapter, ct);
                }
                else if (receipt is not null) await TakeDown(connection, receipt, article.TangentId, did, adapter, ct);
            }

            foreach (var receipt in gone) await TakeDown(connection, receipt, receipt.TangentId, did, adapter, ct);
        }
        catch (Exception failure) when (failure is AtprotoRepositoryException or HttpRequestException or TimeoutException
                                        or OperationCanceledException && !ct.IsCancellationRequested)
        {
            if (failure is AtprotoRepositoryException { NeedsGrant: true })
            {
                connection.NeedsGrant = true;
                connection.Trouble = Ungranted;
                connection.RetryAt = null;
                context.Logger.LogWarning("The Connection of {Participant} needs its grant again", connection.Id);
                return;
            }

            context.Logger.LogWarning(failure, "The Connection of {Participant} could not deliver", connection.Id);
            Troubled(connection, context, failure is AtprotoRepositoryException { Transient: false } ? Refused : Unreachable);
            return;
        }

        connection.Cursor = through;
        connection.Trouble = null;
        connection.RetryAt = null;
        connection.Failures = 0;
        if (rows.Count == Page) context.Reschedule(TimeSpan.Zero);
    }

    /// <summary>A public article of the account the Connection is under against its receipt: published or updated where it
    /// is behind, and one with no receipt only where it publishes on its own (<see cref="Connection.OnItsOwn"/>).</summary>
    private static async Task Deliver(Connection connection, Post article, Receipt? receipt, Publication? publication, AtprotoAdapter adapter,
        CancellationToken ct)
    {
        var wanted = receipt is null ? connection.OnItsOwn(article.LogSeq) : receipt.Retracted || receipt.Behind(article);
        if (!wanted || !Receipt.Theirs(article, receipt)) return;
        var written = await adapter.Deliver(article, receipt, publication, ct);
        await Sent(new KeepReceipt(connection.Id, article.Id, article.TangentId, article.Version, written, DateTimeOffset.UtcNow), ct);
        if (receipt?.IsLive() != true) await Elsewhere.Start(article, ct);
    }

    /// <summary>The copy a receipt names, where its article should not be out — private here, removed, taken down by its
    /// author, or deleted — taken down from the receipt alone, in the repository its address names: that of the account the
    /// Connection is under (<paramref name="did"/>), or that of an earlier account of its participant, through that
    /// account's grant; where that grant no longer lets the place, the receipt says the copy stays there, and nothing tries
    /// again. Nothing goes out under an earlier account, and nothing moves to the new one. A receipt of nothing out is
    /// marked retracted. The receipt records <paramref name="tangentId"/>, where its article stands now, or stood where it is
    /// deleted, so a Tangent counts the copies of what it holds.</summary>
    private static async Task TakeDown(Connection connection, Receipt receipt, string tangentId, string did, AtprotoAdapter adapter, CancellationToken ct)
    {
        var repository = receipt.Repository();
        if (receipt.Retracted || (receipt.Stays && repository != did)) return;
        var retracted = !receipt.IsLive() || await Retracted(receipt, repository!, did, adapter, ct);
        await Sent(new KeepReceipt(connection.Id, receipt.PostId, tangentId, receipt.Version, null, DateTimeOffset.UtcNow, Stays: !retracted), ct);
    }

    /// <summary>Whether the live copy a receipt names came down from <paramref name="repository"/>: at once under the
    /// account the Connection is under, and under an earlier one only through its grant.</summary>
    private static async Task<bool> Retracted(Receipt receipt, string repository, string did, AtprotoAdapter adapter, CancellationToken ct)
    {
        if (repository == did)
        {
            await adapter.Retract(receipt, ct);
            return true;
        }

        if (!await adapter.MayChange(repository, AtprotoAdapter.Scopes, ct)) return false;
        try
        {
            await adapter.Retract(receipt, ct);
            return true;
        }
        catch (AtprotoRepositoryException refused) when (refused.NeedsGrant)
        {
            return false;
        }
    }

    /// <summary>Withdraws the place's publication from the account it is under, through that account's grant where it is
    /// one its proprietor was bound to before; where that grant no longer lets the place, the record stays there. Either
    /// way the place forgets it.</summary>
    private static async Task Withdrawn(Publication publication, string did, AtprotoAdapter adapter, JobContext context, CancellationToken ct)
    {
        try
        {
            if (publication.Did == did || await adapter.MayChange(publication.Did, AtprotoAdapter.ProprietorScopes, ct)) await adapter.Withdraw(publication, ct);
            else context.Logger.LogInformation("The publication {Uri} stays under an earlier account of its proprietor", publication.Uri);
        }
        catch (AtprotoRepositoryException refused) when (refused.NeedsGrant && publication.Did != did)
        {
            context.Logger.LogInformation("The publication {Uri} stays under an earlier account of its proprietor", publication.Uri);
        }

        await Sent(new ForgetPublication(), ct);
    }

    /// <summary>The place as the proprietor's publication under the account <paramref name="did"/> they are bound to: put
    /// where it is missing, under someone else or another account, or says something else now. One under an earlier
    /// account of theirs is withdrawn from it first, where its grant still lets the place.</summary>
    private static async Task<Publication> Placed(TangentSpace space, string did, Publication? publication, AtprotoAdapter adapter, JobContext context,
        CancellationToken ct)
    {
        var fingerprint = adapter.Fingerprint(space);
        var ours = publication is not null && publication.Proprietor == space.ProprietorId && publication.Did == did;
        if (ours && publication!.Fingerprint == fingerprint) return publication;
        if (publication is not null && publication.Proprietor == space.ProprietorId && publication.Did != did)
        {
            await Withdrawn(publication, did, adapter, context, ct);
            publication = null;
        }
        var key = ours ? publication!.Key : Tid.Mint(DateTimeOffset.UtcNow);
        var written = await adapter.Publish(space, did, key, ct);
        var placed = new Publication { Proprietor = space.ProprietorId!, Did = did, Key = key, Uri = written.Uri, Cid = written.Cid, Fingerprint = fingerprint };
        await Sent(new KeepPublication(placed), ct);
        return placed;
    }

    private static async Task Sent<T>(ICommand<T> command, CancellationToken ct)
    {
        if (await command.Send(ct) is Outcome<T>.Failed { Cause: var cause })
            throw new InvalidOperationException("A Connection's receipt could not be kept.", cause);
    }

    /// <summary>Says what the run could not do and tries again later: half a minute after the first failure, twice as long
    /// after each one since, at most an hour.</summary>
    private static void Troubled(Connection connection, JobContext context, string trouble)
    {
        connection.Failures++;
        var wait = TimeSpan.FromSeconds(Math.Min(LongestWait.TotalSeconds, 30 * Math.Pow(2, Math.Min(connection.Failures - 1, 10))));
        connection.Trouble = trouble;
        connection.RetryAt = DateTimeOffset.UtcNow + wait;
        context.Reschedule(wait);
    }
}
