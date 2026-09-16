using Koan.Data.Core;
using Microsoft.Data.Sqlite;
using TangentSpace.Conversation;

internal static class TransactionProbe
{
    internal static async Task<object> Run(SqliteConnection connection, string table, string idColumn,
        Func<long, Post> make, CancellationToken ct)
    {
        static string Q(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        await using (var trigger = connection.CreateCommand())
        {
            trigger.CommandText = $"CREATE TRIGGER epic005_fail_second BEFORE INSERT ON {Q(table)} WHEN NEW.{Q(idColumn)} = 'epic005-tx-second' BEGIN SELECT RAISE(ABORT, 'epic005 injected second-write failure'); END";
            await trigger.ExecuteNonQueryAsync(ct);
        }
        var ids = new[] { "epic005-tx-first", "epic005-tx-second", "epic005-tx-third" };
        string? error = null;
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction("tangent-topic-policy-operation");
            for (var i = 0; i < ids.Length; i++)
            {
                var message = make(200_001 + i); message.Id = ids[i]; message.RoomKey = "epic005-transaction-failure";
                await message.Save(ct);
            }
            await EntityContext.Commit(ct);
        }
        catch (Exception failure) { error = failure.GetType().FullName + ": " + failure.Message; }
        // Read through a newly opened independent SQLite connection after all scopes exit.
        await using var independent = new SqliteConnection(connection.ConnectionString);
        await independent.OpenAsync(ct);
        var persisted = new List<string>();
        await using (var command = independent.CreateCommand())
        {
            command.CommandText = $"SELECT {Q(idColumn)} FROM {Q(table)} WHERE {Q(idColumn)} IN ('epic005-tx-first','epic005-tx-second','epic005-tx-third') ORDER BY {Q(idColumn)}";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) persisted.Add(reader.GetString(0));
        }
        if (error is null) throw new InvalidDataException("The injected failure did not reach deferred commit.");
        Console.WriteLine("Deferred failure persisted: " + string.Join(", ", persisted));
        return new
        {
            scope = "Three Post.Save calls in the application's named EntityContext.Transaction primitive; SQL trigger fails second; independent post-scope connection inspects durable state. Not full acceptance/API or process-crash proof.",
            transactionName = "tangent-topic-policy-operation", queuedIds = ids, failure = error,
            persistedIds = persisted, partialCommitObserved = persisted.SequenceEqual([ids[0]])
        };
    }
}
