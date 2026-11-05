using Koan.Data.Core;
using Koan.Data.Core.Commands;

namespace Tangent.Connections;

/// <summary>A participant chose to publish under their own account, their account having granted the write: their
/// Connection publishes from <see cref="Head"/> on, starting now if it did not exist or had said not now, its trouble
/// cleared, and the article they chose, where they chose one, at <see cref="Version"/>, waits to be published unless it
/// already is, and goes out as theirs at that version even where someone else edited it.</summary>
public sealed record ChooseToPublish(string PersonId, string? PostId, string? TangentId, long Version, long Head, DateTimeOffset At) : ICommand<Connection>
{
    public async Task<Outcome<Connection>> Run(CancellationToken ct)
    {
        var connection = await Connection.Get(PersonId, ct);
        if (connection is null || connection.State == ConnectionState.NotNow)
            connection = new Connection { Id = PersonId, From = Head, Cursor = Head, Since = At };
        connection.State = ConnectionState.Publishing;
        connection.NeedsGrant = false;
        connection.Trouble = null;
        connection.RetryAt = null;
        connection.Failures = 0;
        await connection.Save(ct);

        if (PostId is null) return new Outcome<Connection>.Done(connection);
        var receipt = await Receipt.Get(Receipt.Key(PersonId, PostId), ct);
        if (receipt is null || receipt.Retracted)
            receipt = new Receipt { Id = Receipt.Key(PersonId, PostId), ConnectionId = PersonId, PostId = PostId, TangentId = TangentId ?? "", At = At };
        receipt.Chosen = Math.Max(receipt.Chosen, Version);
        await receipt.Save(ct);

        return new Outcome<Connection>.Done(connection);
    }
}

/// <summary>A participant said not now to the offer to publish under their own account: no article of theirs offers it
/// again until they ask for it back on their Connections page. A Connection that publishes is left as it is.</summary>
public sealed record SayNotNow(string PersonId, DateTimeOffset At) : ICommand<Connection>
{
    public async Task<Outcome<Connection>> Run(CancellationToken ct)
    {
        var connection = await Connection.Get(PersonId, ct);
        if (connection is not null) return new Outcome<Connection>.Done(connection);
        connection = await new Connection { Id = PersonId, State = ConnectionState.NotNow, Since = At }.Save(ct);
        return new Outcome<Connection>.Done(connection);
    }
}

/// <summary>A participant who said not now asks for the offer back: their Connection that never published goes.</summary>
public sealed record OfferAgain(string PersonId) : ICommand<Connection?>
{
    public async Task<Outcome<Connection?>> Run(CancellationToken ct)
    {
        var connection = await Connection.Get(PersonId, ct);
        if (connection is { State: ConnectionState.NotNow }) await connection.Remove(ct);
        return new Outcome<Connection?>.Done(null);
    }
}

/// <summary>What a Connection delivered of an article, or that it retracted it, as its receipt.</summary>
public sealed record KeepReceipt(string ConnectionId, string PostId, string TangentId, long Version, Written? Written, DateTimeOffset At)
    : ICommand<Receipt>
{
    public async Task<Outcome<Receipt>> Run(CancellationToken ct)
    {
        var receipt = await Receipt.Get(Receipt.Key(ConnectionId, PostId), ct)
                      ?? new Receipt { Id = Receipt.Key(ConnectionId, PostId), ConnectionId = ConnectionId, PostId = PostId };
        receipt.TangentId = TangentId;
        receipt.At = At;
        if (Written is { } written)
        {
            receipt.Updated = receipt is { Version: > 0, Retracted: false };
            receipt.Version = Version;
            receipt.Document = written.Document.Uri;
            receipt.DocumentCid = written.Document.Cid;
            receipt.Note = written.Note.Uri;
            receipt.NoteCid = written.Note.Cid;
            receipt.Retracted = false;
        }
        else
        {
            receipt.Retracted = true;
        }

        return new Outcome<Receipt>.Done(await receipt.Save(ct));
    }
}

/// <summary>Forgets the place's publication once its record is withdrawn.</summary>
public sealed record ForgetPublication : ICommand<Publication?>
{
    public async Task<Outcome<Publication?>> Run(CancellationToken ct)
    {
        if (await Publication.Get(Publication.SingleRow, ct) is { } publication) await publication.Remove(ct);
        return new Outcome<Publication?>.Done(null);
    }
}

/// <summary>Keeps where the place stands as its proprietor's publication.</summary>
public sealed record KeepPublication(Publication Row) : ICommand<Publication>
{
    public async Task<Outcome<Publication>> Run(CancellationToken ct) => new Outcome<Publication>.Done(await Row.Save(ct));
}
