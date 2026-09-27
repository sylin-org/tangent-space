using Xunit;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Tangent.Tests.ExperienceIntegration;

/// <summary>Tier B of the cross-server integration: the real Rust connector binary speaking
/// to the real web application over its genuine HTTP contract. Exercises both intakes of the
/// connector hub — the command line and the stdio MCP edge — against accepted seeded history,
/// including service-authored attention, read resynchronization and MCP negotiation. The binary
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
            // The credential the completed bind leaves behind, with its default grant:
            // without it the persona holds no reach and the projected catalog is bare.
            atproto_sessions = new Dictionary<string, object>
            {
                [localId] = new
                {
                    did = ExperienceWebApp.AgentDid,
                    handle = "agent.test",
                    access_jwt = "seeded-not-used",
                    refresh_jwt = (string?)null,
                    pds = app.Origin,
                    authserver = (string?)null,
                    client_id = (string?)null,
                    dpop_key = (string?)null,
                    services = new[] { "tangent" },
                    obtained_at = 1757000000000L,
                },
            },
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

    private JsonElement Invoke(string session, string operation, object arguments)
        => Call("Service_Invoke", JsonSerializer.Serialize(new { session, operation, arguments }));

    [Fact]
    public void The_stored_session_is_accepted_by_the_real_server_and_names_the_real_identity()
    {
        // Sessions are cookie-jar state by design: stored per enrollment in state.json.
        var state = File.ReadAllText(Path.Combine(home, "state.json"));
        Assert.Contains(app.AgentToken, state);
        // And it is a working session: the real server answers arrival on it, naming the
        // identity the server itself resolved rather than anything the connector supplied.
        var connected = Call("Connect", JsonSerializer.Serialize(new { service = "tangent", persona = "agent", address = app.Origin }));
        Assert.Equal("tangent", connected.GetProperty("connector").GetProperty("service").GetString());
        Assert.Contains(ExperienceWebApp.AgentDid, connected.ToString());
    }

    [Fact]
    public async Task The_cli_intake_reads_service_attention_and_resynchronizes_reads()
    {
        // Attention is event-time: the seeded writes' compiled projection must land before
        // the arrival digest can carry it.
        Assert.NotNull(await app.AwaitAttentionAsync(ExperienceWebApp.TopicKey, app.AgentParticipantId,
            row => row.UnreadCount >= 2, TimeSpan.FromSeconds(10)));
        var arrived = Call("Connect", JsonSerializer.Serialize(new { service = "tangent", persona = "agent", address = app.Origin }));
        var connectorLayer = arrived.GetProperty("connector");
        Assert.Equal("tangent", connectorLayer.GetProperty("service").GetString());
        var context = connectorLayer.GetProperty("contextId").GetString()!;
        // Canonical identity flows through the service-owned structured object, with the
        // directed attention items riding along in the arrival digest.
        Assert.Equal(ExperienceWebApp.AgentDid,
            arrived.GetProperty("service").GetProperty("identity").GetProperty("did").GetString());
        var arrivalAttention = arrived.GetProperty("service").GetProperty("attention");
        Assert.Equal(2, arrivalAttention.GetProperty("waitingCount").GetProperty("value").GetInt32());
        Assert.Contains(arrivalAttention.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("relationship").GetString() == "addressed_to_you");

        var updates = Invoke(context, "get_updates", new { limit = 25 });
        Assert.Equal(2, updates.GetProperty("service").GetProperty("attention")
            .GetProperty("waitingCount").GetProperty("value").GetInt32());

        var read = Invoke(context, "read_topic", new { topicKey = ExperienceWebApp.TopicKey });
        var posts = read.GetProperty("service").GetProperty("result").GetProperty("data").GetProperty("posts");
        Assert.Equal(3, posts.GetArrayLength());
        var cursor = read.GetProperty("service").GetProperty("continuation").GetProperty("readCursor").GetString()!;

        Invoke(context, "read_position", new { topicKey = ExperienceWebApp.TopicKey, readCursor = cursor });
        // The acknowledgement resynchronizes the projection on its own compile; await it
        // before the catch-up renders the post-read state.
        Assert.NotNull(await app.AwaitAttentionAsync(ExperienceWebApp.TopicKey, app.AgentParticipantId,
            row => row.LastSequence > 0 && row.ReadSequence >= row.LastSequence, TimeSpan.FromSeconds(10)));
        var after = Invoke(context, "get_updates", new { limit = 25 });
        Assert.Equal(0, after.GetProperty("service").GetProperty("attention")
            .GetProperty("waitingCount").GetProperty("value").GetInt32());
    }

    [Fact]
    public void The_connector_advertises_a_small_generic_surface_and_the_service_catalog()
    {
        using var peer = ConnectorPeer.Start(ConnectorBinary(), home);
        peer.Initialize();
        var names = peer.Tools().EnumerateArray().Select(entry => entry.GetProperty("name").GetString()).ToHashSet();
        Assert.Equal(new[] { "Connect", "ListCompanions", "Service_Invoke", "Service_Operations", "WhoAmI" },
            names.OrderBy(name => name).ToArray());
        var connected = peer.Call("tools/call", new { name = "Connect", arguments = new { service = "tangent", persona = "agent", address = app.Origin } });
        Assert.False(connected.GetProperty("result").GetProperty("isError").GetBoolean());
        var session = connected.GetProperty("result").GetProperty("structuredContent")
            .GetProperty("connector").GetProperty("contextId").GetString();
        var catalog = peer.Call("tools/call", new { name = "Service_Operations", arguments = new { session } });
        var operations = catalog.GetProperty("result").GetProperty("structuredContent")
            .GetProperty("service").GetProperty("operations");
        Assert.Contains(operations.EnumerateArray(), item => item.GetProperty("id").GetString() == "get_updates");
        Assert.Contains(operations.EnumerateArray(), item => item.GetProperty("id").GetString() == "manage_user");
    }

    [Fact]
    public void Service_operations_write_and_read_through_the_real_binary()
    {
        var context = Connect();
        var started = Invoke(context, "start_thread", new
        {
            tangentKey = ExperienceWebApp.TangentKey, requestId = "bin-thread-1",
            title = "Binary Topic", text = "Started from the connector binary.",
        });
        var data = started.GetProperty("service").GetProperty("result").GetProperty("data");
        var topicRef = data.GetProperty("topicRef").GetString()!;
        var postRef = data.GetProperty("postRef").GetString()!;
        Assert.StartsWith(app.Origin, topicRef);

        var read = Invoke(context, "read_participant", new { identifier = ExperienceWebApp.HumanDid });
        var profile = read.GetProperty("service").GetProperty("result").GetProperty("data").GetProperty("profile");
        Assert.Equal(ExperienceWebApp.HumanHandle, profile.GetProperty("handle").GetString());

        // Edit-own and delete-own ride the domain's own-post rules; the Topic must allow editing.
        EnableEditing(topicRef.Split("::")[^1]);
        Invoke(context, "edit_post", new { postRef, text = "Edited from the binary." });
        var window = Invoke(context, "read_topic", new { topicKey = topicRef.Split("::")[^1] });
        Assert.Equal("Edited from the binary.", window.GetProperty("service").GetProperty("result")
            .GetProperty("data").GetProperty("posts")[0].GetProperty("text").GetString());
        Invoke(context, "delete_post", new { postRef });
        var after = Invoke(context, "read_topic", new { topicKey = topicRef.Split("::")[^1] });
        Assert.True(after.GetProperty("service").GetProperty("result").GetProperty("data")
            .GetProperty("posts")[0].GetProperty("removed").GetBoolean());
    }

    private string Connect()
    {
        var arrived = Call("Connect", JsonSerializer.Serialize(new { service = "tangent", persona = "agent", address = app.Origin }));
        return arrived.GetProperty("connector").GetProperty("contextId").GetString()!;
    }

    private void EnableEditing(string topicKey)
    {
        app.Http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", app.OwnerToken);
        using var settings = app.Http.PatchAsync($"/api/topics/{topicKey}/settings",
            JsonContent.Create(new { allowPostEditing = true, isLocked = false }), TestContext.Current.CancellationToken).Result;
        settings.EnsureSuccessStatusCode();
        app.Http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", app.AgentToken);
    }

    [Fact]
    public async Task The_stdio_mcp_intake_negotiates_and_serves_the_full_flow()
    {
        // The arrival greeting renders the compiled attention; await the compile first.
        Assert.NotNull(await app.AwaitAttentionAsync(ExperienceWebApp.TopicKey, app.AgentParticipantId,
            row => row.UnreadCount >= 2, TimeSpan.FromSeconds(10)));
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
        Assert.Equal(5, tools.GetProperty("result").GetProperty("tools").GetArrayLength());

        var connected = peer.Call("tools/call", new { name = "Connect", arguments = new { service = "tangent", persona = "agent", address = app.Origin } });
        var arrivalText = connected.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("You are agent — session tangent_", arrivalText);
        Assert.Contains("Tangent", arrivalText);

        var who = peer.Call("tools/call", new { name = "WhoAmI", arguments = new { session = "tangent_00000000" } });
        Assert.True(who.GetProperty("result").GetProperty("isError").GetBoolean());

        var updates = peer.Call("tools/call", new { name = "Service_Invoke", arguments = new
        { session = "tangent_00000000", operation = "get_updates", arguments = new { } } });
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
            while (true)
            {
                var read = output.ReadLineAsync();
                if (Task.WhenAny(read, deadline).Result != read)
                    throw new TimeoutException($"MCP call timed out: {method}");
                using var response = JsonDocument.Parse(read.Result!);
                // A notification (a projected-catalog change, say) may ride the stream ahead of
                // the answer; the response id is what pairs.
                if (!response.RootElement.TryGetProperty("id", out var matched)) continue;
                Assert.Equal(id, matched.GetInt32());
                return response.RootElement.Clone();
            }
        }

        public void Notify(string method, object parameters)
        {
            input.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters }));
        }

        /// <summary>The negotiated handshake, so a test can list tools without repeating it.</summary>
        public void Initialize()
        {
            var initialize = Call("initialize", new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "dotnet-integration", version = "1" },
            });
            Assert.Equal("2025-06-18", initialize.GetProperty("result").GetProperty("protocolVersion").GetString());
            Notify("notifications/initialized", new { });
        }

        /// <summary>The live projected catalog — projection runs per call, never declared.</summary>
        public JsonElement Tools()
        {
            var tools = Call("tools/list", new { });
            return tools.GetProperty("result").GetProperty("tools").Clone();
        }

        public void Dispose()
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            process.Dispose();
        }
    }
}
