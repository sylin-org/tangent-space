using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

/// <summary>
/// The current Companion Lobby binary, with a completed enrollment in its current state,
/// exercises its typed Forum adapter against Tangent's real HTTP resource routes.
/// Proof minting is covered separately by the identity exchange tests.
/// </summary>
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

    private async Task SeedEnrollment()
    {
        const string handle = "agent";
        var localId = Guid.CreateVersion7().ToString("n");
        var enrollmentId = "cmp_" + localId[..16];
        var credentialId = "cred_atproto_" + localId;
        const long enrolledAt = 1_757_000_000_000;
        var state = new
        {
            schemaVersion = 3,
            companions = new Dictionary<string, object>
            {
                [localId] = new { local_id = localId, handle, display_name = (string?)null,
                    bound_did = ExperienceWebApp.AgentDid, created_at = enrolledAt }
            },
            server_cards = new Dictionary<string, object>(),
            manager_page_url = (string?)null,
            enrollments = new[]
            {
                new {
                    enrollment_id = enrollmentId, service_id = "tangent",
                    place = new { kind = "origin", key = app.Origin, address = app.Origin },
                    local_id = localId, credential_id = credentialId,
                    credential_lifecycle_epoch = 0, session_generation = 1,
                    name = handle, origin = app.Origin, participant_ref = app.AgentParticipantId,
                    did = ExperienceWebApp.AgentDid, display_name = (string?)null,
                    handle = ExperienceWebApp.AgentHandle, enrolled_at = enrolledAt
                }
            },
            enrollment_keys = new Dictionary<string, string>
            {
                [$"{localId}\u001ftangent\u001forigin\u001f{app.Origin}\u001f{credentialId}"] = enrollmentId
            },
            sessions = new Dictionary<string, string> { [enrollmentId] = app.AgentToken },
            atproto_sessions = new Dictionary<string, object>
            {
                [localId] = new {
                    did = ExperienceWebApp.AgentDid, handle = ExperienceWebApp.AgentHandle,
                    access_jwt = "unused-seeded-pds-session", refresh_jwt = (string?)null,
                    pds = app.Origin, authserver = (string?)null, client_id = "http://127.0.0.1:5229/oauth-client-metadata",
                    dpop_key = (string?)null, services = new[] { "tangent" },
                    scope_state = "unverified", granted_scopes = Array.Empty<string>(),
                    credential_id = credentialId, credential_lifecycle_epoch = 0,
                    obtained_at = enrolledAt
                }
            },
            contexts = Array.Empty<object>(),
            bindings = new Dictionary<string, object>
            {
                [credentialId] = new {
                    credentialId, companionId = localId,
                    account = new { authServiceId = "atproto", accountKey = ExperienceWebApp.AgentDid },
                    safeAccountLabel = ExperienceWebApp.AgentHandle, status = "connected",
                    effectiveGrants = new[] { "tangent" }, dormantGrants = Array.Empty<object>(),
                    lifecycleEpoch = 0, sessionGeneration = 1, authMaterialRevision = 1,
                    grantRevision = 0
                }
            },
            connection_sign_ins = new Dictionary<string, object>(),
            connection_health = new Dictionary<string, string>(),
            ownership_reservations = new Dictionary<string, object>(),
            activity = Array.Empty<object>(),
            conditions = Array.Empty<object>(),
            activity_high_water = 0,
            state_revision = 0,
            store_id = "store-integration-test",
            stream_epoch = "stream-integration-test"
        };
        await File.WriteAllTextAsync(Path.Combine(home, "state.json"), JsonSerializer.Serialize(state));
    }

    private static string ConnectorBinary()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "compose.yaml")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var crate = Path.Combine(directory.Parent!.FullName, "CompanionLobby");
        // The debug binary is built by Companion Lobby's current test/build workflow.
        // Require the debug binary built from the current Companion Lobby checkout.
        var candidate = Path.Combine(crate, "target", "debug",
            OperatingSystem.IsWindows() ? "companion-lobby.exe" : "companion-lobby");
        if (File.Exists(candidate)) return candidate;
        throw new FileNotFoundException(
            $"Build the current Companion Lobby binary with cargo build -p companion-lobby: {candidate}");
    }

    [Fact]
    public async Task Typed_forum_tools_read_and_write_through_the_real_resource_API()
    {
        Assert.NotNull(await app.AwaitAttentionAsync(ExperienceWebApp.TopicKey, app.AgentParticipantId,
            row => row.UnreadCount >= 2, TimeSpan.FromSeconds(10)));
        using var peer = ConnectorPeer.Start(ConnectorBinary(), home);
        peer.Initialize();
        var connected = peer.Tool("Connect", new
            { service = "tangent", persona = "agent", address = app.Origin });
        Assert.False(connected.GetProperty("isError").GetBoolean(), connected.ToString());
        var names = peer.Tools().EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()).ToHashSet();
        Assert.Contains("Forum_Arrive", names);
        Assert.Contains("Forum_ReadDiscussion", names);
        Assert.Contains("Forum_CreatePost", names);
        Assert.DoesNotContain("Service_Invoke", names);
        Assert.DoesNotContain("Service_Operations", names);
        var session = connected.GetProperty("structuredContent").GetProperty("connector")
            .GetProperty("contextId").GetString()!;
        Assert.StartsWith("tangent_", session);
        var arrival = connected.GetProperty("structuredContent").GetProperty("forum");
        Assert.Equal("arrival", arrival.GetProperty("kind").GetString());
        Assert.Equal(ExperienceWebApp.AgentDid, arrival.GetProperty("data")
            .GetProperty("connection").GetProperty("identity").GetProperty("externalId").GetString());

        var community = $"{app.Origin}::{ExperienceWebApp.TangentKey}";
        var discussion = $"{community}::{ExperienceWebApp.TopicKey}";
        var communities = peer.Tool("Forum_ListCommunities", new { session });
        Assert.Equal("communities", Forum(communities).GetProperty("kind").GetString());
        Assert.Contains(Forum(communities).GetProperty("data").GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetString() == community);

        var topics = peer.Tool("Forum_ListDiscussions", new { session, community });
        Assert.Contains(Forum(topics).GetProperty("data").GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetString() == discussion);
        var read = peer.Tool("Forum_ReadDiscussion", new { session, discussion });
        Assert.Equal("discussion", Forum(read).GetProperty("kind").GetString());
        Assert.Equal(3, Forum(read).GetProperty("data").GetProperty("posts").GetArrayLength());

        var updates = peer.Tool("Forum_CatchUp", new { session });
        Assert.Equal("updates", Forum(updates).GetProperty("kind").GetString());
        Assert.Equal(2, Forum(updates).GetProperty("data").GetProperty("waitingCount").GetInt32());

        var requestId = "bin-post-" + Guid.CreateVersion7().ToString("n")[..12];
        var created = peer.Tool("Forum_CreatePost", new {
            session, discussion, requestId, text = "Posted through the current typed Forum adapter."
        });
        var post = Forum(created);
        Assert.Equal("post", post.GetProperty("kind").GetString());
        Assert.Equal(requestId, post.GetProperty("data").GetProperty("requestId").GetString());
        Assert.StartsWith(discussion + "::", post.GetProperty("data").GetProperty("post").GetString());
        var replay = peer.Tool("Forum_CreatePost", new {
            session, discussion, requestId, text = "Posted through the current typed Forum adapter."
        });
        Assert.Equal(post.GetProperty("data").GetProperty("post").GetString(),
            Forum(replay).GetProperty("data").GetProperty("post").GetString());

        var cursor = Forum(read).GetProperty("data").GetProperty("readCursor").GetString()!;
        var position = peer.Tool("Forum_SetReadPosition", new { session, discussion, readCursor = cursor });
        Assert.Equal("read_position", Forum(position).GetProperty("kind").GetString());
    }

    [Fact]
    public void Typed_forum_rejects_unknown_context_without_reaching_another_participant()
    {
        using var peer = ConnectorPeer.Start(ConnectorBinary(), home);
        peer.Initialize();
        var failed = peer.Tool("Forum_Arrive", new { session = "tangent_00000000" });
        Assert.True(failed.GetProperty("isError").GetBoolean());
        Assert.Equal("context_expired", failed.GetProperty("structuredContent")
            .GetProperty("problem").GetProperty("code").GetString());
    }

    private static JsonElement Forum(JsonElement result)
    {
        Assert.False(result.GetProperty("isError").GetBoolean(), result.ToString());
        return result.GetProperty("structuredContent").GetProperty("forum");
    }

    private sealed class ConnectorPeer : IDisposable
    {
        private readonly Process process;
        private readonly StreamReader output;
        private readonly StreamWriter input;
        private int nextId = 1;

        private ConnectorPeer(Process process)
        {
            this.process = process;
            output = process.StandardOutput;
            input = process.StandardInput;
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
            information.Environment["COMPANION_LOBBY_PORT"] = "5229";
            var process = Process.Start(information)!;
            _ = process.StandardError.ReadToEndAsync();
            return new ConnectorPeer(process);
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
                if (!response.RootElement.TryGetProperty("id", out var matched)) continue;
                Assert.Equal(id, matched.GetInt32());
                Assert.True(response.RootElement.TryGetProperty("result", out var result), response.RootElement.ToString());
                return result.Clone();
            }
        }

        public JsonElement Tool(string name, object arguments)
            => Call("tools/call", new { name, arguments });

        public void Notify(string method, object parameters)
            => input.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters }));

        public void Initialize()
        {
            var initialize = Call("initialize", new {
                protocolVersion = "2025-06-18", capabilities = new { },
                clientInfo = new { name = "dotnet-integration", version = "1" }
            });
            Assert.Equal("2025-06-18", initialize.GetProperty("protocolVersion").GetString());
            Notify("notifications/initialized", new { });
        }

        public JsonElement Tools() => Call("tools/list", new { }).GetProperty("tools").Clone();

        public void Dispose()
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            process.Dispose();
        }
    }
}
