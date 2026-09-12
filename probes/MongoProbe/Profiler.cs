using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;

internal sealed class Profiler(IMongoDatabase database, string collection)
{
    private IMongoCollection<BsonDocument> Records => database.GetCollection<BsonDocument>("system.profile");
    private FilterDefinition<BsonDocument> Scope => new BsonDocument("ns", database.DatabaseNamespace.DatabaseName + "." + collection);
    internal static JsonElement Json(BsonDocument doc) => JsonDocument.Parse(doc.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson })).RootElement.Clone();
    internal async Task<string> Begin(CancellationToken ct)
    {
        var marker = "epic005-capture-" + Guid.NewGuid().ToString("N");
        await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "profile", 2 }, { "slowms", 0 }, { "sampleRate", 1.0 } }, cancellationToken: ct);
        await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "find", collection }, { "filter", new BsonDocument("_id", marker) }, { "limit", 1 }, { "singleBatch", true }, { "comment", marker } }, cancellationToken: ct);
        return marker;
    }
    internal Task Disable(CancellationToken ct) => database.RunCommandAsync<BsonDocument>(new BsonDocument("profile", 0), cancellationToken: ct);
    internal async Task<object[]> End(string marker, bool explain, CancellationToken ct)
    {
        await Disable(ct);
        var all = await Records.Find(Scope).Sort(new BsonDocument("$natural", 1)).Limit(1000).ToListAsync(ct);
        var markerIndex = all.FindIndex(row => row.GetValue("command", new BsonDocument()).AsBsonDocument.GetValue("comment", "").ToString() == marker);
        // A retained unique start marker proves subsequent capped records have not rolled off.
        if (markerIndex < 0 || all.Count == 1000) throw new InvalidOperationException("Profiler capture marker missing or retention bound reached; trace is inconclusive.");
        var result = new List<object>();
        foreach (var row in all.Skip(markerIndex + 1))
        {
            var command = row.GetValue("command", new BsonDocument()).AsBsonDocument;
            var clean = new BsonDocument();
            foreach (var name in new[] { "find", "filter", "sort", "limit", "skip", "aggregate", "pipeline", "cursor", "getMore", "collection", "update", "updates", "delete", "deletes", "insert", "documents", "ordered" })
                if (command.Contains(name)) clean[name] = command[name].DeepClone();
            JsonElement? plan = null;
            if (explain && (clean.Contains("find") || clean.Contains("aggregate")))
            {
                var explanation = await database.RunCommandAsync<BsonDocument>(new BsonDocument { { "explain", clean }, { "verbosity", "executionStats" } }, cancellationToken: ct);
                plan = Json(explanation);
            }
            result.Add(new { captureMarker = marker, captureStartRetained = true, operation = row.GetValue("op", "").ToString(), command = Json(clean),
                millis = row.GetValue("millis", BsonNull.Value).ToString(), docsExamined = row.GetValue("docsExamined", BsonNull.Value).ToString(),
                keysExamined = row.GetValue("keysExamined", BsonNull.Value).ToString(), returned = row.GetValue("nreturned", BsonNull.Value).ToString(),
                planSummary = row.GetValue("planSummary", BsonNull.Value).ToString(), explanation = plan });
        }
        return result.ToArray();
    }
    internal static void AssertReadTrace(object[] records, bool expectCount)
    {
        var commands = JsonSerializer.SerializeToElement(records).EnumerateArray().Select(row => row.GetProperty("command")).ToArray();
        if (commands.Count(command => command.TryGetProperty("find", out _)) != 1
            || commands.Count(command => command.TryGetProperty("aggregate", out _)) != (expectCount ? 1 : 0)
            || commands.Length != (expectCount ? 2 : 1))
            throw new InvalidDataException("Unexpected query trace; expected one find and " + (expectCount ? "one count aggregate." : "no count aggregate."));
    }
}
