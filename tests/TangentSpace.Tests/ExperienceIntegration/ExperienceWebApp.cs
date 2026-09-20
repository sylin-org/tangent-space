using Koan.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Koan.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using System.Net;
using System.Net.Sockets;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Activity;
using Tangent.Identity;
using Tangent.Stewardship;
using Tangent.Spaces;

namespace Tangent.Tests.ExperienceIntegration;

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

    public static async Task<ExperienceWebApp> StartAsync()
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
            ["Tangent:Space:Name"] = "Experience Integration Space",
            ["Tangent:Space:OwnerDid"] = OwnerDid,
            ["Tangent:Space:PublicOrigin"] = origin,
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={Path.Combine(root, "space.sqlite")}",
            ["Koan:Data:Sqlite:ConnectionString"] = $"Data Source={Path.Combine(root, "space.sqlite")}",
            ["Koan:Cache:Adapters:Sqlite:DatabasePath"] = Path.Combine(root, "cache.db"),
        });
        builder.Services.AddKoan();
        builder.Services.AddKoanJobs(o => o.Mode = Koan.Jobs.JobMode.Inline);
        // The test host's entry assembly is this test project; add the application's
        // controllers explicitly so route discovery matches the deployed app.
        builder.Services.AddControllers().AddApplicationPart(typeof(Program).Assembly);
        var app = builder.Build();
        app.Urls.Add(origin);
        await app.StartAsync();
        AppHost.Current = app.Services;
        var (token, credentialId, ownerToken, ownerParticipant, agentParticipant, humanParticipant) = await SeedAsync(app.Services);
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
    /// three accepted posts: the agent's own question, Leo's direct reply to it, and Leo's
    /// fresh post mentioning the agent. Each is a Post row with its sequence, exactly like
    /// the acceptance path.</summary>
    private static async Task<(string Token, string CredentialId, string OwnerToken, string OwnerParticipant, string AgentParticipant, string HumanParticipant)> SeedAsync(IServiceProvider services)
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
        await server.Claim(OwnerParticipant, OwnerDid, CancellationToken.None);
        var roles = services.GetRequiredService<RoleCollection>();
        await roles.Add(TangentBuiltInRoles.Member.Token, AgentParticipant, CancellationToken.None);
        await roles.Add(TangentBuiltInRoles.Member.Token, HumanParticipant, CancellationToken.None);
        var tangents = services.GetRequiredService<TangentGovernance>();
        var companions = services.GetRequiredService<ParticipantGovernance>();
        await tangents.Create(OwnerParticipant, TangentKey, "Workshop", "Integration workshop", null, null, null,
            CancellationToken.None, TangentAdmission.Open);
        // The workshop models an operator's configured space: members read, post and start
        // threads. The default tangent grants reading alone (the category table).
        await tangents.SetAccess(OwnerParticipant, TangentKey, new AccessMap
        {
            See = ["role:member"], Post = ["role:member"], CreateTopics = ["role:member"]
        }, CancellationToken.None);
        await tangents.CreateTopic(OwnerParticipant, TangentKey, TopicKey, "Project Z",
            "Coordinate Project Z", CancellationToken.None);
        await companions.Join(AgentParticipant, TangentKey, CancellationToken.None);

        var agentPostId = await Accept(TopicKey, AgentParticipant, "agent-q1", 1, now.AddMinutes(-30),
            "I can review the Project Z plan if someone summarizes the open questions.", null);
        await Accept(TopicKey, HumanParticipant, "leo-r1", 2, now.AddMinutes(-20),
            "Here is the summary: three open questions remain.", agentPostId);
        await Accept(TopicKey, HumanParticipant, "leo-m1", 3, now.AddMinutes(-5),
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

    internal static async Task<string> Accept(string roomKey, string authorId, string recordKey, long sequence,
        DateTimeOffset acceptedAt, string text, string? replyTo)
    {
        var post = new Post
        {
            Id = "m-" + recordKey, TopicKey = roomKey, AuthorParticipantId = authorId, Sequence = sequence,
            AcceptedAt = acceptedAt, Content = new PostContent(text, acceptedAt, replyTo),
        };
        using var context = EntityContext.NoCache();
        await post.Save();
        var conversation = await TopicConversation.Get(roomKey) ?? new TopicConversation { Id = roomKey };
        conversation.LastSequence = Math.Max(conversation.LastSequence, sequence);
        await conversation.Save();
        // The acceptance path journals the write, which is what signals the attention compile.
        await ActivityJournal.AppendInTransaction(ActivityKind.PostAccepted, roomKey, authorId,
            messageSequence: sequence, occurredAt: acceptedAt);
        return post.Id;
    }

    /// <summary>Waits, bounded, for the topic's compiled attention row for one participant to
    /// reach the given state. The compile runs in the background off the journal's signal, so
    /// tests that read attention through the projection await it instead of sleeping.</summary>
    public async Task<TopicAttention?> AwaitAttentionAsync(string topicKey, string participantId,
        Func<TopicAttention, bool>? ready = null, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTimeOffset.UtcNow < deadline)
        {
            var row = await TopicAttention.Get(TopicAttention.IdOf(topicKey, participantId), CancellationToken.None);
            if (row is not null && (ready is null || ready(row))) return row;
            // Inline jobs submitted inside a transaction wait for a drain that is not
            // mid-transaction; re-trigger the compile so the test stays deterministic.
            try
            {
                var coordinator = Services.GetRequiredService<Koan.Jobs.IJobCoordinator>();
                await coordinator.TriggerAsync("Tangent.Activity.CompileTopicAttention", "", CancellationToken.None);
            }
            catch
            {
                // The re-trigger is best-effort; the poll is the actual wait.
            }
            await Task.Delay(100, CancellationToken.None);
        }
        return null;
    }

    /// <summary>Revoke the agent credential mid-test to prove private content is withdrawn.</summary>
    public async Task RevokeAgentCredential()
    {
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
