using Koan.Data.Abstractions;
using Koan.Data.Core;
using Npgsql;
using TangentSpace.Conversation;

internal static class TransactionHealth
{
    private static bool IsInjected(Exception error) => error is PostgresException postgres && postgres.SqlState == "P0001" && postgres.MessageText == "epic005 injected second-write failure"
        || error.InnerException is not null && IsInjected(error.InnerException)
        || error is AggregateException aggregate && aggregate.InnerExceptions.Any(IsInjected);
    internal static async Task<object> Run(NpgsqlConnection native, string connectionString, string table, string idColumn, CancellationToken ct)
    {
        static string Q(string value) => '"' + value.Replace("\"", "\"\"") + '"';
        var schema = table.Split('.')[0];
        await using (var command = new NpgsqlCommand($"CREATE FUNCTION {schema}.epic005_fail_second() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.{Q(idColumn)} IN ('epic005-atomic-second','epic005-deferred-second') THEN RAISE EXCEPTION 'epic005 injected second-write failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER epic005_fail_second BEFORE INSERT OR UPDATE ON {table} FOR EACH ROW EXECUTE FUNCTION {schema}.epic005_fail_second()", native)) await command.ExecuteNonQueryAsync(ct);
        Post Item(string id, long sequence) { var item = Checks.Make("transaction", sequence); item.Id = id; return item; }
        var success = Post.Batch(); success.Add(Item("epic005-batch-ok-first", 200001)).Add(Item("epic005-batch-ok-second", 200002));
        BatchResult receipt; using (EntityContext.NoCache()) receipt = await success.Save(new BatchOptions(RequireAtomic: true, MaxItems: 3), ct);
        if (receipt.Atomicity != BatchAtomicity.Atomic || receipt.Added != 2) throw new InvalidDataException("Atomic batch returned a non-atomic/incorrect receipt.");
        async Task<string[]> Persisted(string prefix)
        {
            await using var fresh = new NpgsqlConnection(connectionString); await fresh.OpenAsync(ct);
            await using var command = new NpgsqlCommand($"SELECT {Q(idColumn)} FROM {table} WHERE {Q(idColumn)} LIKE @pattern ORDER BY {Q(idColumn)}", fresh); command.Parameters.AddWithValue("pattern", prefix + "%");
            var ids = new List<string>(); await using var reader = await command.ExecuteReaderAsync(ct); while (await reader.ReadAsync(ct)) ids.Add(reader.GetString(0)); return ids.ToArray();
        }
        var successfulIds = await Persisted("epic005-batch-ok-"); if (successfulIds.Length != 2) throw new InvalidDataException("Atomic success rows not durable.");
        string? atomicError = null;
        try { using var scope = EntityContext.NoCache(); var failure = Post.Batch(); failure.Add(Item("epic005-atomic-first", 200011)).Add(Item("epic005-atomic-second", 200012)).Add(Item("epic005-atomic-third", 200013)); await failure.Save(new BatchOptions(RequireAtomic: true, MaxItems: 3), ct); }
        catch (Exception error) { if (!IsInjected(error)) throw new InvalidDataException("Atomic batch failed for a reason other than the injected second write.", error); atomicError = error.GetType().Name + ": " + error.Message; }
        var atomicIds = await Persisted("epic005-atomic-");
        if (atomicError is null || atomicIds.Length != 0) throw new InvalidDataException("Native same-entity batch failed rollback qualification.");
        string? deferredError = null;
        try
        {
            using var scope = EntityContext.NoCache(); using var transaction = EntityContext.Transaction("tangent-room-policy-operation");
            foreach (var (id, sequence) in new[] { ("epic005-deferred-first", 200021L), ("epic005-deferred-second", 200022L), ("epic005-deferred-third", 200023L) }) await Item(id, sequence).Save(ct);
            await EntityContext.Commit(ct);
        }
        catch (Exception error) { if (!IsInjected(error)) throw new InvalidDataException("Deferred coordinator failed for a reason other than the injected second write.", error); deferredError = error.GetType().Name + ": " + error.Message; }
        if (deferredError is null) throw new InvalidDataException("Injected deferred-write failure was not reached.");
        var deferredIds = await Persisted("epic005-deferred-");
        if (!deferredIds.SequenceEqual(["epic005-deferred-first"])) throw new InvalidDataException("Deferred coordinator's durable failure result changed; investigate before reporting the expected partial-commit baseline.");
        return new { nativeSameEntityBatch = new { executionCapability = success.ExecutionCapabilities.ToString(), atomicity = receipt.Atomicity.ToString(), successIds = successfulIds, failure = atomicError, rowsAfterFailure = atomicIds, rollbackPassed = atomicIds.Length == 0 },
            ambientCoordinator = new { scope = "Three same-entity Post.Save calls in application's named deferred coordinator, second SQL write rejected; independent connection inspects durable state. This is not a cross-entity domain acceptance test.", failure = deferredError, rowsAfterFailure = deferredIds, partialCommitObserved = deferredIds.SequenceEqual(["epic005-deferred-first"]) } };
    }
}
