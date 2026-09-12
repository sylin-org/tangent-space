using Koan.Data.Abstractions;
using Koan.Data.Core;
using MongoDB.Bson;
using MongoDB.Driver;
using TangentSpace.Activity;
using TangentSpace.Conversation;

internal static class Admission
{
    internal static async Task<object> Crud(CancellationToken ct)
    {
        var message = Fixture.Make("crud", 17);
        using (EntityContext.NoCache()) await message.Save(ct);
        Message loaded;
        using (EntityContext.NoCache()) loaded = await Message.Get(message.Id, ct) ?? throw new InvalidDataException("CRUD create not visible.");
        if (loaded.Content != message.Content || loaded.Sequence != message.Sequence) throw new InvalidDataException("CRUD Unicode/date roundtrip failed.");
        loaded.Content = loaded.Content with { Text = "Edited café 日本語" }; loaded.EditedAt = Fixture.Epoch.AddDays(2); loaded.Removed = true;
        using (EntityContext.NoCache()) await loaded.Save(ct);
        using (EntityContext.NoCache())
        {
            var updated = await Message.Get(message.Id, ct);
            if (updated?.Content.Text != loaded.Content.Text || !updated.Removed || updated.EditedAt != loaded.EditedAt) throw new InvalidDataException("CRUD update failed.");
            if (!await Message.Remove(message.Id, ct) || await Message.Get(message.Id, ct) is not null) throw new InvalidDataException("CRUD delete failed.");
        }
        return new { create = true, read = true, unicodeAndDateRoundtrip = true, editAndTombstone = true, delete = true };
    }
    internal static async Task<object> Atomic(IMongoCollection<BsonDocument> collection, Profiler profiler, CancellationToken ct)
    {
        var existing = Fixture.Make("atomic-update", 1); var deleted = Fixture.Make("atomic-delete", 1); var added = Fixture.Make("atomic-new", 1);
        using (EntityContext.NoCache()) { await existing.Save(ct); await deleted.Save(ct); }
        var filter = new BsonDocument("_id", new BsonDocument("$in", new BsonArray { existing.Id, deleted.Id, added.Id }));
        var before = await collection.Find(filter).Sort(new BsonDocument("_id", 1)).ToListAsync(ct);
        var invoked = false; string? error = null;
        var profileBefore = await profiler.Begin(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            await Message.Batch().Add(added).Update(existing.Id, m => { invoked = true; m.Content = m.Content with { Text = "Must not run" }; }).Delete(deleted.Id)
                .Save(new BatchOptions(RequireAtomic: true, MaxItems: 3), ct);
        }
        catch (NotSupportedException rejected) when (rejected.Message is "This MongoDB batch has not proved transaction support for its selected topology; atomic execution is unavailable."
            or "The adapter backing Message does not expose a proved native atomic batch boundary. Remove RequireAtomic or route the Entity to an adapter that advertises and executes atomic batches.")
        { error = rejected.GetType().FullName + ": " + rejected.Message; }
        var commands = await profiler.End(profileBefore, false, ct);
        var after = await collection.Find(filter).Sort(new BsonDocument("_id", 1)).ToListAsync(ct);
        var unchanged = before.Select(v => v.ToJson()).SequenceEqual(after.Select(v => v.ToJson()));
        if (error is null || invoked || !unchanged || commands.Length != 0) throw new InvalidDataException("RequireAtomic failed to reject before lifecycle reads or mutation.");
        return new { rejected = true, error, mutationCallbackInvoked = invoked, documentsUnchanged = unchanged, observedCommands = commands, batchCapabilities = Message.Batch().ExecutionCapabilities.ToString() };
    }
    internal static async Task<object> Deferred(IMongoDatabase database, IMongoCollection<BsonDocument> messages, CancellationToken ct)
    {
        var anchor = new ActivityHead { Id = "mongo-guard-initial", LastSequence = 0, UpdatedAt = Fixture.Epoch };
        using (EntityContext.NoCache()) await anchor.Save(ct);
        const string headCollection = "TangentSpace.Activity.ActivityHead";
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "collMod", headCollection }, { "validator", new BsonDocument("lastSequence", new BsonDocument("$lte", 10)) },
            { "validationLevel", "strict" }, { "validationAction", "error" }
        }, cancellationToken: ct);
        var first = Fixture.Make("deferred-first", 1); var third = Fixture.Make("deferred-third", 1);
        string? failure = null; var providerValidationFailure = false;
        try
        {
            using var fresh = EntityContext.NoCache(); using var scope = EntityContext.Transaction("tangent-room-policy-operation");
            await first.Save(ct);
            await new ActivityHead { Id = "mongo-deferred-rejected-head", LastSequence = 999, UpdatedAt = Fixture.Epoch }.Save(ct);
            await third.Save(ct); await EntityContext.Commit(ct);
        }
        catch (Exception error)
        {
            var chain = new List<string>();
            for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            {
                chain.Add(cause.GetType().FullName + ": " + cause.Message);
                if (cause is MongoWriteException write && write.WriteError.Code == 121) providerValidationFailure = true;
                if (cause is MongoBulkWriteException<BsonDocument> bulk && bulk.WriteErrors.Any(writeError => writeError.Code == 121)) providerValidationFailure = true;
                if (cause is MongoCommandException command && command.Code == 121) providerValidationFailure = true;
            }
            failure = string.Join(" ---> ", chain);
        }
        var firstPresent = await messages.CountDocumentsAsync(new BsonDocument("_id", first.Id), cancellationToken: ct) == 1;
        var thirdPresent = await messages.CountDocumentsAsync(new BsonDocument("_id", third.Id), cancellationToken: ct) == 1;
        var headPresent = await database.GetCollection<BsonDocument>(headCollection).CountDocumentsAsync(new BsonDocument("_id", "mongo-deferred-rejected-head"), cancellationToken: ct) == 1;
        if (failure is null || !providerValidationFailure
            || !firstPresent || headPresent || thirdPresent)
            throw new InvalidDataException($"Deferred fault result inconclusive or unexpected: failure={failure}; first={firstPresent}; head={headPresent}; third={thirdPresent}.");
        return new { failure, providerValidationFailure, firstMessageDurable = firstPresent, rejectedHeadDurable = headPresent, thirdMessageDurable = thirdPresent,
            partialCommitObserved = firstPresent && !headPresent && !thirdPresent,
            scope = "Actual Message → ActivityHead → Message saves in EntityContext deferred scope; external driver observes durable state. No native transaction invoked and no complete acceptance/restart proof." };
    }
}
