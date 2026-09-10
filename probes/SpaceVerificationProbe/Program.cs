using System.Security.Cryptography;
using System.Text.Json;
using SpaceVerificationProbe;

var directory = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppContext.BaseDirectory, "../../../../fixtures");
using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json")));
var vectors = manifest.RootElement.GetProperty("primitiveVectors");
var state = SpaceCarVerifier.ComputeSetState(vectors.GetProperty("elements").EnumerateArray().Select(x => x.GetString()!));
var ctx = vectors.GetProperty("ctx");
var encoded = SpaceCarVerifier.EncodeContext(ctx.GetProperty("space").GetString()!, ctx.GetProperty("author").GetString()!, ctx.GetProperty("rev").GetString()!, Convert.FromHexString(vectors.GetProperty("ikmHex").GetString()!));
var primitiveChecks = new {
    ltHashLanesMatch = Convert.ToHexStringLower(state) == vectors.GetProperty("stateHex").GetString(),
    ltHashDigestMatches = Convert.ToHexStringLower(SHA256.HashData(state)) == vectors.GetProperty("digestHex").GetString(),
    encodedContextMatches = Convert.ToHexStringLower(encoded) == vectors.GetProperty("encodedContextHex").GetString(),
};
var results = new List<object>();
int failed = primitiveChecks.ltHashLanesMatch && primitiveChecks.ltHashDigestMatches && primitiveChecks.encodedContextMatches ? 0 : 1;
foreach (var test in manifest.RootElement.GetProperty("tests").EnumerateArray())
{
    bool accepted = false; string? error = null; int? records = null;
    var expected = test.GetProperty("expected").GetBoolean();
    try {
        var author = test.GetProperty("author").GetString()!;
        var key = new ResolvedAuthorKey(author, test.GetProperty("curve").GetString()!, Convert.FromHexString(test.GetProperty("publicKeyHex").GetString()!));
        var result = SpaceCarVerifier.Verify(await File.ReadAllBytesAsync(Path.Combine(directory, test.GetProperty("file").GetString()!)), test.GetProperty("space").GetString()!, author, key, test.GetProperty("expectValues").GetBoolean());
        accepted = true; records = result.Records.Count;
    } catch (Exception e) { error = e.Message; }
    var passed = accepted == expected; if (!passed) failed++;
    results.Add(new { name = test.GetProperty("name").GetString(), expected, accepted, passed, records, error });
}
var evidence = new { completedAt = DateTimeOffset.UtcNow, upstreamRevision = manifest.RootElement.GetProperty("upstreamRevision").GetString(), runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, native = true, primitiveChecks, cases = results.Count, failures = failed, results };
var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (args.Length > 1) await File.WriteAllTextAsync(Path.GetFullPath(args[1]), json);
return failed == 0 ? 0 : 1;
