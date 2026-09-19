using Xunit;
using System.Diagnostics;
using System.Text.Json;

namespace Tangent.Tests.ExperienceIntegration;

/// <summary>Tier B of the cross-server integration: the real Rust connector binary speaking
/// to the real web application over its genuine HTTP contract. Exercises both intakes of the
/// connector hub — the command line and the stdio MCP edge — against accepted seeded history,
/// including you-rendered attention, read resynchronization and MCP negotiation. The binary
/// comes from the standard Build action (server-lifecycle.ps1 builds the web image and the connector together).</summary>
[Xunit.Collection("Experience integration")]
public sealed class ConnectorIntegrationTests : IAsyncLifetime
{
    private ExperienceWebApp app = null!;
    private string home = null!;

    public async ValueTask InitializeAsync()
    {
        app = await ExperienceWebApp.StartAsync();
        home = Path.Combine(Path.GetTempPath(), "TangentSpace-Connector", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(home);
        await SeedEnrollment();
    }

    public async ValueTask DisposeAsync()
    {
        await app.DisposeAsync();
        try { Directory.Delete(home, recursive: true); } catch (IOException) { }
    }

    /// <summary>Seeds the connector's durable state with one enrolled identity and its
    /// session — the state the account-bound exchange leaves behind. The exchange itself is
    /// the connector suite's and <c>EnrollmentTests</c>' subject; these tests are about the
    /// two intakes speaking to the real server over a real session.</summary>
    private async Task SeedEnrollment()
    {
        var localId = Guid.CreateVersion7().ToString("n");
        var enrollmentId = "cmp_" + Guid.CreateVersion7().ToString("n")[..16];
        // The connector's state file is snake_case on the wire, matching its Rust structs.
        var state = new
        {
            companions = new[]
            {
                new { local_id = localId, handle = "agent", display_name = (string?)null, bound_did = ExperienceWebApp.AgentDid, created_at = 1757000000000L },
            },
            enrollments = new[]
            {
                new
                {
                    enrollment_id = enrollmentId, local_id = localId, name = "agent", origin = app.Origin,
                    participant_ref = ExperienceWebApp.AgentDid, did = ExperienceWebApp.AgentDid,
                    display_name = (string?)null, handle = "agent", enrolled_at = 1757000000000L, auto_check = true,
                },
            },
            sessions = new Dictionary<string, string> { [enrollmentId] = app.AgentToken },
        };
        await File.WriteAllTextAsync(Path.Combine(home, "state.json"),
            JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string ConnectorBinary()
    {
        // Walk up to this repository's root, then across to the sibling CompanionLobby checkout.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "compose.yaml")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var crate = Path.Combine(directory.Parent!.FullName, "CompanionLobby");
        foreach (var profile in new[] { "release", "debug" })
        {
            var candidate = Path.Combine(crate, "target", profile,
                OperatingSystem.IsWindows() ? "companion-lobby.exe" : "companion-lobby");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(
            $"The companion-lobby binary was not found under {crate}/target. Run Build.bat (or cargo build --release in the CompanionLobby workspace) before these integration tests.");
    }

    private (int Exit, string Output, string Error) Run(params string[] arguments)
    {
        var information = new ProcessStartInfo(ConnectorBinary())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // ArgumentList applies correct platform quoting; JSON arguments contain quotes.
        foreach (var argument in arguments) information.ArgumentList.Add(argument);
        information.Environment["COMPANION_LOBBY_HOME"] = home;
        information.Environment["COMPANION_LOBBY_NO_BROWSER"] = "1";
        using var process = Process.Start(information)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill();
            throw new TimeoutException($"connector {arguments.FirstOrDefault()} did not exit in time");
        }
        return (process.ExitCode, output.Result, error.Result);
    }

    private JsonElement Call(string tool, string argumentsJson)
    {
        var (exit, output, error) = Run("call", tool, argumentsJson, "--json");
        if (exit != 0)
            Assert.Fail($"connector call {tool} failed (exit {exit}): {error}{output}");
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }

    [Fact]
    public void The_stored_session_is_accepted_by_the_real_server_and_names_the_real_identity()
    {
        // Sessions are cookie-jar state by design: stored per enrollment in state.json.
        var state = File.ReadAllText(Path.Combine(home, "state.json"));
        Assert.Contains(app.AgentToken, state);
        // And it is a working session: the real server answers arrival on it, naming the
        // identity the server itself resolved rather than anything the connector supplied.
        var selected = Call("SelectCompanion", "{\"moniker\":\"agent\"}");
        var companion = selected.GetProperty("connector").GetProperty("enrollmentId").GetString()!;
        var arrived = Call("Arrive", JsonSerializer.Serialize(new { enrollmentId = companion, serverUrl = app.Origin }));
        Assert.Equal("orientation", arrived.GetProperty("connector").GetProperty("view").GetString());
        Assert.Contains(ExperienceWebApp.AgentDid, arrived.ToString());
    }

    [Fact]
    public void The_cli_intake_reads_you_rendered_attention_and_resynchronizes_reads()
    {
        var selected = Call("SelectCompanion", "{\"moniker\":\"agent\"}");
        var companion = selected.GetProperty("connector").GetProperty("enrollmentId").GetString()!;
        var arrived = Call("Arrive", JsonSerializer.Serialize(new { enrollmentId = companion, serverUrl = app.Origin }));
        var connectorLayer = arrived.GetProperty("connector");
        Assert.Equal("orientation", connectorLayer.GetProperty("view").GetString());
        Assert.Equal("tool_response_only", connectorLayer.GetProperty("deliveryMode").GetString());
        var context = connectorLayer.GetProperty("contextId").GetString()!;
        // Canonical identity flows through the structured experience object, with the
        // directed attention items riding along in the arrival digest.
        Assert.Equal(ExperienceWebApp.AgentDid,
            arrived.GetProperty("experience").GetProperty("identity").GetProperty("did").GetString());
        var arrivalAttention = arrived.GetProperty("experience").GetProperty("attention");
        Assert.Equal(2, arrivalAttention.GetProperty("waitingCount").GetProperty("value").GetInt32());
        Assert.Contains(arrivalAttention.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("relationship").GetString() == "addressed_to_you");

        // The rendered view keeps the you perspective over real data, previews in the
        // expanded view (compact shows the count line only, per the response budget).
        var (_, text, _) = Run("call", "GetUpdates",
            JsonSerializer.Serialize(new { contextId = context, view = "expanded" }));
        Assert.Contains("asked you", text);
        Assert.Contains("replied to you", text);

        var read = Call("ReadTopic", JsonSerializer.Serialize(new { topicRef = app.TopicRef, contextId = context }));
        var posts = read.GetProperty("experience").GetProperty("result").GetProperty("data").GetProperty("posts");
        Assert.Equal(3, posts.GetArrayLength());
        var cursor = read.GetProperty("experience").GetProperty("continuation").GetProperty("readCursor").GetString()!;

        Call("MarkRead", JsonSerializer.Serialize(new { contextId = context, topicRef = app.TopicRef, readCursor = cursor }));
        var (_, after, _) = Run("call", "GetUpdates", JsonSerializer.Serialize(new { contextId = context }));
        Assert.DoesNotContain("asked you", after);
        Assert.DoesNotContain("replied to you", after);
        Assert.Contains("Waiting for you: 0", after);
    }

    [Fact]
    public void The_stdio_mcp_intake_negotiates_and_serves_the_full_flow()
    {
        using var peer = ConnectorPeer.Start(ConnectorBinary(), home);
        var initialize = peer.Call("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "dotnet-integration", version = "1" },
        });
        Assert.Equal("2025-06-18", initialize.GetProperty("result").GetProperty("protocolVersion").GetString());
        peer.Notify("notifications/initialized", new { });
        var tools = peer.Call("tools/list", new { });
        Assert.Equal(14, tools.GetProperty("result").GetProperty("tools").GetArrayLength());

        var selected = peer.Call("tools/call", new { name = "SelectCompanion", arguments = new { moniker = "agent" } });
        var companion = selected.GetProperty("result").GetProperty("structuredContent").GetProperty("connector")
            .GetProperty("enrollmentId").GetString()!;
        var arrival = peer.Call("tools/call", new { name = "Arrive", arguments = new { enrollmentId = companion, serverUrl = app.Origin } });
        var arrivalText = arrival.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("you are participating as", arrivalText);
        Assert.Contains("asked you", arrivalText);

        var updates = peer.Call("tools/call", new { name = "GetUpdates", arguments = new { contextId = "ctx_missing" } });
        // Unknown context ids must fail closed, not leak another context's state.
        Assert.True(updates.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal("context_expired",
            updates.GetProperty("result").GetProperty("structuredContent").GetProperty("problem").GetProperty("code").GetString());
    }

    /// <summary>A minimal stdio JSON-RPC client over the connector's real transport.</summary>
    private sealed class ConnectorPeer : IDisposable
    {
        private readonly Process process;
        private readonly StreamReader output;
        private readonly StreamWriter input;
        private int nextId = 1;

        private ConnectorPeer(Process process, StreamReader output, StreamWriter input)
        {
            this.process = process;
            this.output = output;
            this.input = input;
        }

        public static ConnectorPeer Start(string binary, string home)
        {
            var information = new ProcessStartInfo(binary)
            {
                Arguments = "serve",
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            information.Environment["COMPANION_LOBBY_HOME"] = home;
            information.Environment["COMPANION_LOBBY_NO_BROWSER"] = "1";
            // Its own fixed page port, so an operator's running connector keeps 5219.
            information.Environment["COMPANION_LOBBY_PORT"] = "5229";
            var process = Process.Start(information)!;
            _ = process.StandardError.ReadToEndAsync(); // drain to avoid pipe blocking
            return new ConnectorPeer(process, process.StandardOutput, process.StandardInput);
        }

        public JsonElement Call(string method, object parameters)
        {
            var id = nextId++;
            input.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }));
            var deadline = Task.Delay(TimeSpan.FromSeconds(30));
            var read = output.ReadLineAsync();
            if (Task.WhenAny(read, deadline).Result != read)
                throw new TimeoutException($"MCP call timed out: {method}");
            using var response = JsonDocument.Parse(read.Result!);
            Assert.Equal(id, response.RootElement.GetProperty("id").GetInt32());
            return response.RootElement.Clone();
        }

        public void Notify(string method, object parameters)
        {
            input.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters }));
        }

        public void Dispose()
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            process.Dispose();
        }
    }
}
