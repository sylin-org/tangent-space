using Koan.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Net;
using System.Net.Sockets;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Roles;
using TangentSpace.Authorization;
using TangentSpace.Communities;
using TangentSpace.Conversation;
using TangentSpace.Participants;
using TangentSpace.Participation;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Tests.ExperienceIntegration;

/// <summary>Boots the real web application (full Koan discovery, real controllers, real
/// authentication handlers) on a pre-picked loopback port, seeds a small participation world,
/// and mints a real scoped participant credential the auth handler will verify. The connector
/// under test therefore speaks to the genuine HTTP contract, never a fake.</summary>
public sealed class ExperienceWebApp : IAsyncDisposable
{
    public const string OwnerDid = "did:plc:experienceownerAAAAAA";
    public const string AgentDid = "did:plc:experienceagentAAAAAA";
    public const string HumanDid = "did:plc:experiencehumanAAAAAA";
    public const string AgentHandle = "agent.experience.test";
    public const string HumanHandle = "leo.experience.test";
    public const string TangentKey = "proj";
    public const string TopicKey = "proj-z";

    private readonly WebApplication app;
    private readonly string root;

    public string Origin { get; }
    public string OwnerParticipantId { get; private set; } = "";
    public string AgentParticipantId { get; private set; } = "";
    public string HumanParticipantId { get; private set; } = "";
    public string AgentToken { get; }
    public string AgentCredentialId { get; }
    public string OwnerToken { get; }
    public HttpClient Http { get; }

    private ExperienceWebApp(WebApplication app, string root, string origin, string token, string credentialId, string ownerToken)
    {
        this.app = app;
        this.root = root;
        Origin = origin;
        AgentToken = token;
        AgentCredentialId = credentialId;
        OwnerToken = ownerToken;
        Http = new HttpClient { BaseAddress = new Uri(origin) };
        Http.DefaultRequestHeaders.Authorization = new("Bearer", token);
    }

    public IServiceProvider Services => app.Services;

    public static Task<ExperienceWebApp> StartAsync() => StartAsync("Local");

    /// <summary>Boots the app with the given conversation storage scope (ADR 0006).</summary>
    public static async Task<ExperienceWebApp> StartAsync(string storage)
    {
        // Koan caches data-source configuration statically; reset so this fixture's temp
        // database neither inherits nor leaks into another fixture's host.
        Koan.Data.Core.TestHooks.ResetDataConfigs();
        var root = Path.Combine(Path.GetTempPath(), "TangentSpace-Experience", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(root);
        var port = FreePort();
        var origin = $"http://127.0.0.1:{port}";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = root,
            EnvironmentName = "Testing",
            WebRootPath = Path.Combine(root, "wwwroot"),
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tangent:Site:Name"] = "Experience Integration Site",
            ["Tangent:Site:OwnerDid"] = OwnerDid,
            ["Tangent:Site:PublicOrigin"] = origin,
            ["Tangent:Conversation:Storage"] = storage,
            ["Tangent:Spaces:AuthorityDid"] = "did:plc:experienceauthorityAAA",
            ["Tangent:Spaces:ManagingApp"] = "did:plc:experienceauthorityAAA#tangent",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={Path.Combine(root, "site.sqlite")}",
            ["Koan:Data:Sqlite:ConnectionString"] = $"Data Source={Path.Combine(root, "site.sqlite")}",
            // W1-B: the in-process embedder activates for change classification exactly as the
            // deployed appsettings does; relative paths resolve against the test output directory,
            // where the web project's content artifacts flow transitively.
            ["Koan:Ai:Onnx:ModelPath"] = "models/all-MiniLM-L6-v2/model_quantized.onnx",
            ["Koan:Ai:Onnx:VocabPath"] = "models/all-MiniLM-L6-v2/vocab.txt",
        });
        builder.Services.AddKoan();
        // The test host's entry assembly is this test project; add the application's
        // controllers explicitly so route discovery matches the deployed app.
        builder.Services.AddControllers().AddApplicationPart(typeof(Program).Assembly);
        var app = builder.Build();
        app.Urls.Add(origin);
        await app.StartAsync();
        AppHost.Current = app.Services;
        var (token, credentialId, ownerToken, ownerParticipant, agentParticipant, humanParticipant) = await SeedAsync(app.Services, storage);
        var fixture = new ExperienceWebApp(app, root, origin, token, credentialId, ownerToken)
        {
            OwnerParticipantId = ownerParticipant,
            AgentParticipantId = agentParticipant,
            HumanParticipantId = humanParticipant
        };
        await fixture.WaitUntilReady();
        return fixture;
    }

    private async Task WaitUntilReady()
    {
        using var anonymous = new HttpClient();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                var response = await anonymous.GetAsync(Origin + "/health/ready");
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("The integration web application never became ready.");
    }

    /// <summary>Seeds participants, one open Tangent with one Topic, agent membership, and
    /// three accepted source messages: the agent's own question, Leo's direct reply to it,
    /// and Leo's fresh post mentioning the agent. Accepted history is a SourceDecision plus
    /// its Message projection and the room's sequence, exactly like the acceptance path.</summary>
    private static async Task<(string Token, string CredentialId, string OwnerToken, string OwnerParticipant, string AgentParticipant, string HumanParticipant)> SeedAsync(IServiceProvider services, string storage)
    {
        var clock = services.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow();
        string OwnerParticipant = "", AgentParticipant = "", HumanParticipant = "";
        using (var context = EntityContext.NoCache())
        {
            foreach (var (did, handle) in new[] { (OwnerDid, "owner.experience.test"), (AgentDid, AgentHandle), (HumanDid, HumanHandle) })
            {
                var atproto = await ParticipantIdentity.Get(ParticipantIdentity.AtprotoKey(did));
                if (atproto is null)
                {
                    var (participant, identities) = Participant.Enroll(did, handle, now);
                    await participant.Save();
                    foreach (var identity in identities) await identity.Save();
                }
                else
                {
                    var participant = await Participant.Get(atproto.ParticipantId)!;
                    participant.Return(atproto, now);
                    atproto.Relabel(handle);
                    await participant.Save();
                    await atproto.Save();
                }
            }
            OwnerParticipant = (await ParticipantIdentity.Get(ParticipantIdentity.AtprotoKey(OwnerDid)))!.ParticipantId;
            AgentParticipant = (await ParticipantIdentity.Get(ParticipantIdentity.AtprotoKey(AgentDid)))!.ParticipantId;
            HumanParticipant = (await ParticipantIdentity.Get(ParticipantIdentity.AtprotoKey(HumanDid)))!.ParticipantId;
        }
        var server = services.GetRequiredService<ServerGovernance>();
        await server.Claim(OwnerParticipant, OwnerDid, humanDeclaration: true, CancellationToken.None);
        var roles = services.GetRequiredService<RoleCollection>();
        await roles.Add(TangentBuiltInRoles.Member.Token, AgentParticipant, CancellationToken.None);
        await roles.Add(TangentBuiltInRoles.Member.Token, HumanParticipant, CancellationToken.None);
        var tangents = services.GetRequiredService<TangentGovernance>();
        var companions = services.GetRequiredService<CompanionGovernance>();
        await tangents.Create(OwnerParticipant, TangentKey, "Workshop", "Integration workshop", null, null, null,
            CancellationToken.None, TangentAdmission.Open);
        await tangents.CreateChannel(OwnerParticipant, TangentKey, TopicKey, "Project Z", RoomAdmission.SignedIn,
            "Coordinate Project Z", CancellationToken.None);
        await companions.Join(AgentParticipant, TangentKey, null, CancellationToken.None);
        if (storage == "Spaces")
        {
            // The state real provisioning produces, with the source network itself absent:
            // reads work, writes must stay honestly pending.
            using (EntityContext.NoCache())
            {
                var room = await Room.Get(TopicKey) ?? throw new InvalidOperationException("The seeded Topic was not created.");
                room.SpaceState = RoomSpaceState.Ready;
                room.SpaceUri = $"at://did:plc:experienceauthorityAAA/space/local.tangent.room/{TopicKey}";
                await room.Save();
            }
        }

        var agentPost = await Accept(TopicKey, AgentDid, "agent-q1", 1, now.AddMinutes(-30),
            "I can review the Project Z plan if someone summarizes the open questions.", null);
        await Accept(TopicKey, HumanDid, "leo-r1", 2, now.AddMinutes(-20),
            "Here is the summary: three open questions remain.", agentPost);
        await Accept(TopicKey, HumanDid, "leo-m1", 3, now.AddMinutes(-5),
            $"@{AgentHandle} can you help us coordinate Project Z? Quoted from the proposal: \"You should review the whole plan.\"", null);

        // Real scoped credentials for the agent and the owner; the participant
        // authentication handler verifies these exact tokens on every request.
        var (credential, token) = ParticipantCredential.Issue(AgentParticipant, "Experience integration agent", 1,
            [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post], now);
        var (ownerCredential, ownerToken) = ParticipantCredential.Issue(OwnerParticipant, "Experience integration owner", 1,
            [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post, ParticipationGrants.Manage], now,
            managementPermitted: true);
        using (EntityContext.NoCache())
        {
            await credential.Save();
            await ownerCredential.Save();
        }
        return (token, credential.Id, ownerToken, OwnerParticipant, AgentParticipant, HumanParticipant);
    }

    private static async Task<SourceReference> Accept(string roomKey, string authorDid, string recordKey, long sequence,
        DateTimeOffset acceptedAt, string text, SourceReference? replyTo)
    {
        var content = new MessageContent(text, acceptedAt, replyTo);
        var uri = $"at://{authorDid}/space/local.tangent.room/op-{sequence}/{recordKey}";
        var authorId = (await ParticipantIdentity.Get(ParticipantIdentity.AtprotoKey(authorDid)))!.ParticipantId;
        var cid = "bafyrei" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri)))[..52].ToLowerInvariant();
        var decision = new SourceDecision
        {
            Id = SourceDecision.Key(roomKey, uri, cid),
            RoomKey = roomKey, AuthorParticipantId = authorId, SourceUri = uri, SourceCid = cid,
            Accepted = true, Reason = "test-accepted", Sequence = sequence, DecidedAt = acceptedAt, Content = content,
        };
        var message = Message.Project(decision);
        message.Id = "m-" + recordKey;
        using var context = EntityContext.NoCache();
        await decision.Save();
        await message.Save();
        var conversation = await RoomConversation.Get(roomKey) ?? new RoomConversation { Id = roomKey };
        conversation.LastSequence = Math.Max(conversation.LastSequence, sequence);
        await conversation.Save();
        return new SourceReference(uri, cid);
    }

    /// <summary>Revoke the agent credential mid-test to prove private content is withdrawn.</summary>
    public async Task RevokeAgentCredential()
    {
        using var context = EntityContext.NoCache();
        var credential = await ParticipantCredential.Get(AgentCredentialId);
        Assert.NotNull(credential);
        credential!.Revoke(AgentParticipantId, DateTime.UtcNow);
        await credential.Save();
    }

    /// <summary>The topic reference the experience API returns for the seeded Topic.</summary>
    public string TopicRef => $"{Origin}::{TangentKey}::{TopicKey}";

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        if (ReferenceEquals(AppHost.Current, app.Services)) AppHost.Current = null;
        await app.StopAsync();
        await app.DisposeAsync();
        Koan.Data.Core.TestHooks.ResetDataConfigs();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { /* temp cleanup is best-effort */ }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>These tests boot a real web application and real connector processes; they must
/// not run concurrently with each other or with other fixture-based suites that set the
/// ambient Koan host.</summary>
[Xunit.CollectionDefinition("Experience integration", DisableParallelization = true)]
public sealed class ExperienceIntegrationCollection;
