using System.Text.Json;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TangentSpace.Activity;
using TangentSpace.Authorization;
using TangentSpace.Communities;
using TangentSpace.Conversation;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Site;
using Xunit;

namespace TangentSpace.Tests;

/// <summary>Real service calls and isolated SQLite persistence; no server workers or network.
/// The collection serializes Koan's ambient data-source configuration with other host tests.</summary>
[Collection("Experience integration")]
public sealed class HumanHostAccountabilityTests
{
    private const string OwnerDid = "did:plc:humanhostowneraaaaaaaaaa";
    private const string OtherDid = "did:plc:humanhostotheraaaaaaaaa";

    [Theory]
    [InlineData(false, ParticipantClassification.Agent)]
    [InlineData(false, ParticipantClassification.Undeclared)]
    [InlineData(true, ParticipantClassification.Agent)]
    [InlineData(true, ParticipantClassification.Undeclared)]
    public async Task Both_declaration_paths_preserve_the_human_Host_owner(bool ownerReviewed, ParticipantClassification desired)
    {
        using var fixture = new Fixture();
        var owner = await fixture.Enroll(OwnerDid);
        await fixture.Server.Claim(owner, OwnerDid, true, fixture.Ct);
        var before = await fixture.Snapshot(owner);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Declare(ownerReviewed, owner, owner, OwnerDid, desired));

        Assert.Equal(before, await fixture.Snapshot(owner));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_classification_is_rejected_without_changing_owner_or_journal(bool ownerReviewed)
    {
        using var fixture = new Fixture();
        var owner = await fixture.Enroll(OwnerDid);
        await fixture.Server.Claim(owner, OwnerDid, true, fixture.Ct);
        var before = await fixture.Snapshot(owner);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Declare(ownerReviewed, owner, owner, OwnerDid, (ParticipantClassification)999));

        Assert.Equal(before, await fixture.Snapshot(owner));
    }

    [Theory]
    [InlineData(false, ParticipantClassification.Agent, false)]
    [InlineData(false, ParticipantClassification.Undeclared, true)]
    [InlineData(false, ParticipantClassification.Human, true)]
    [InlineData(true, ParticipantClassification.Agent, false)]
    [InlineData(true, ParticipantClassification.Undeclared, true)]
    [InlineData(true, ParticipantClassification.Human, true)]
    public async Task Both_declaration_paths_reject_current_and_historic_agents_becoming_human(
        bool ownerReviewed, ParticipantClassification current, bool wasAgent)
    {
        using var fixture = new Fixture();
        var owner = await fixture.Enroll(OwnerDid);
        await fixture.Server.Claim(owner, OwnerDid, true, fixture.Ct);
        var agent = await fixture.Enroll(OtherDid, current, wasAgent);
        var before = await fixture.Snapshot(agent);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Declare(ownerReviewed, owner, agent, OtherDid, ParticipantClassification.Human));

        Assert.Equal(before, await fixture.Snapshot(agent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_review_cannot_clear_agent_classification_then_allow_a_human_self_declaration(bool wasAgent)
    {
        using var fixture = new Fixture();
        var owner = await fixture.Enroll(OwnerDid);
        await fixture.Server.Claim(owner, OwnerDid, true, fixture.Ct);
        var agent = await fixture.Enroll(OtherDid, ParticipantClassification.Agent, wasAgent);
        var before = await fixture.Snapshot(agent);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Companions.DeclareClassification(owner, OtherDid, ParticipantClassification.Undeclared, fixture.Ct));
        Assert.Equal(before, await fixture.Snapshot(agent));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Server.Declare(agent, ParticipantClassification.Human, fixture.Ct));
        Assert.Equal(before, await fixture.Snapshot(agent));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Companions.DeclareClassification(owner, OtherDid, ParticipantClassification.Human, fixture.Ct));
        Assert.Equal(before, await fixture.Snapshot(agent));
    }

    [Theory]
    [InlineData(ParticipantClassification.Agent, false)]
    [InlineData(ParticipantClassification.Undeclared, true)]
    [InlineData(ParticipantClassification.Human, true)]
    public async Task Current_and_historic_agents_cannot_claim_or_be_offered_Host_ownership(
        ParticipantClassification current, bool wasAgent)
    {
        using var fixture = new Fixture();
        var actor = await fixture.Enroll(OwnerDid, current, wasAgent);
        var before = await fixture.Snapshot(actor);

        Assert.False((await fixture.Server.Read(actor, fixture.Ct)).CanClaim);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Server.Claim(actor, OwnerDid, true, fixture.Ct));

        Assert.Equal(before, await fixture.Snapshot(actor));
    }

    [Theory]
    [InlineData("missing-declaration")]
    [InlineData("missing-sign-in")]
    [InlineData("wrong-holder")]
    [InlineData("configured-owner")]
    [InlineData("already-owned")]
    public async Task Refused_claims_do_not_classify_the_caller_or_change_site_or_journal(string reason)
    {
        using var fixture = new Fixture(reason == "configured-owner" ? OtherDid : "");
        var actor = await fixture.Enroll(OwnerDid);
        var other = await fixture.Enroll(OtherDid);
        if (reason == "already-owned") await fixture.Server.Claim(other, OtherDid, true, fixture.Ct);
        var before = await fixture.Snapshot(actor);
        var suppliedDid = reason == "missing-sign-in" ? null : reason == "wrong-holder" ? OtherDid : OwnerDid;

        Func<Task> claim = () => fixture.Server.Claim(actor, suppliedDid, reason != "missing-declaration", fixture.Ct);
        if (reason is "missing-sign-in" or "wrong-holder") await Assert.ThrowsAsync<UnauthorizedAccessException>(claim);
        else await Assert.ThrowsAsync<InvalidOperationException>(claim);

        Assert.Equal(before, await fixture.Snapshot(actor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_human_owner_can_reaffirm_the_declaration_through_either_path(bool ownerReviewed)
    {
        using var fixture = new Fixture();
        var owner = await fixture.Enroll(OwnerDid);
        Assert.True((await fixture.Server.Read(owner, fixture.Ct)).CanClaim);
        await fixture.Server.Claim(owner, OwnerDid, true, fixture.Ct);

        await fixture.Declare(ownerReviewed, owner, owner, OwnerDid, ParticipantClassification.Human);

        using var fresh = EntityContext.NoCache();
        var participant = await Participant.Get(owner, fixture.Ct);
        var site = await TangentSite.Get(TangentConstants.SiteId, fixture.Ct);
        Assert.Equal(ParticipantClassification.Human, participant!.Classification);
        Assert.False(participant.WasDeclaredAgent);
        Assert.Equal(owner, site!.OwnerParticipantId);
        Assert.True(site.HumanDeclared);
    }

    [Fact]
    public async Task Suspended_owner_cannot_reclassify_another_participant()
    {
        using var fixture = new Fixture();
        var owner = await fixture.Enroll(OwnerDid);
        await fixture.Server.Claim(owner, OwnerDid, true, fixture.Ct);
        var other = await fixture.Enroll(OtherDid);
        using (EntityContext.NoCache())
        {
            var participant = (await Participant.Get(owner, fixture.Ct))!;
            participant.IsSuspended = true;
            await participant.Save(fixture.Ct);
        }
        var before = await fixture.Snapshot(other);

        var error = await Assert.ThrowsAsync<TangentRuleViolation>(() =>
            fixture.Companions.DeclareClassification(owner, OtherDid, ParticipantClassification.Agent, fixture.Ct));

        Assert.Equal(TangentDenial.Forbidden, error.Denial);
        Assert.Equal(before, await fixture.Snapshot(other));
    }

    [Fact]
    public async Task Agent_Tangent_ownership_remains_available_under_the_human_Host_policy()
    {
        using var fixture = new Fixture();
        var owner = await fixture.Enroll(OwnerDid);
        await fixture.Server.Claim(owner, OwnerDid, true, fixture.Ct);
        var agent = await fixture.Enroll(OtherDid, ParticipantClassification.Agent, true);
        await fixture.Server.SetAccess(owner, new AccessMap
        {
            See = [AccessTokens.Everyone], Manage = [], CreateTangents = [AccessTokens.Authenticated]
        }, fixture.Ct);
        await fixture.Server.Update(owner, new ServerSettingsPatch(AllowAgentTangentOwnership: true), fixture.Ct);

        var tangent = await fixture.Tangents.Create(agent, "agent-place", "Agent-owned place", null, null, null, null, fixture.Ct);

        Assert.Equal(agent, tangent.OwnerParticipantId);
        using var fresh = EntityContext.NoCache();
        Assert.Equal(owner, (await TangentSite.Get(TangentConstants.SiteId, fixture.Ct))!.OwnerParticipantId);
        Assert.Equal(ParticipantClassification.Agent, (await Participant.Get(agent, fixture.Ct))!.Classification);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly IHost host;
        private readonly PolicyGate gate = new();
        private readonly string root = Path.Combine(Path.GetTempPath(), "TangentSpace-HumanHost", Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        public CancellationToken Ct => timeout.Token;
        public ServerGovernance Server { get; }
        public CompanionGovernance Companions { get; }
        public TangentGovernance Tangents { get; }

        public Fixture(string ownerDid = "")
        {
            Directory.CreateDirectory(root);
            TestHooks.ResetDataConfigs();
            var connection = $"Data Source={Path.Combine(root, "accountability.sqlite")};Pooling=False";
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root, EnvironmentName = "Testing", Args = [] });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = connection,
                ["Koan:Data:Sqlite:ConnectionString"] = connection,
                ["Koan:Identity:Posture"] = "Closed",
                ["Koan:Identity:SeedDevUsers"] = "false"
            });
            builder.Logging.ClearProviders();
            builder.Services.AddKoan();
            host = builder.Build(); // No StartAsync: this fixture never launches workers/listeners.
            AppHost.Current = host.Services;
            SQLitePCL.Batteries_V2.Init();
            var directory = new ParticipantDirectory(TimeProvider.System, new NoHandles());
            var roles = host.Services.GetRequiredService<RoleCollection>();
            var roleAccess = new TangentRoleAccess(roles);
            Server = new(TimeProvider.System, gate, Options.Create(new SiteOptions { OwnerDid = ownerDid }), directory, roleAccess);
            Companions = new(TimeProvider.System, gate, new RoomGovernance(TimeProvider.System, gate, directory, roleAccess), directory);
            Tangents = new(TimeProvider.System, gate, Options.Create(new ConversationOptions()), directory, roleAccess);
        }

        public async Task<string> Enroll(string did, ParticipantClassification classification = ParticipantClassification.Undeclared, bool wasAgent = false)
        {
            using var fresh = EntityContext.NoCache();
            var (participant, identities) = Participant.Enroll(did, "accountability.test", DateTimeOffset.UtcNow);
            // Seed legacy/inconsistent combinations deliberately to test historic-agent guards.
            participant.Classification = classification;
            participant.WasDeclaredAgent = wasAgent;
            await participant.Save(Ct);
            foreach (var identity in identities) await identity.Save(Ct);
            return participant.Id;
        }

        public Task Declare(bool ownerReviewed, string owner, string target, string targetDid, ParticipantClassification classification)
            => ownerReviewed ? Companions.DeclareClassification(owner, targetDid, classification, Ct) : Server.Declare(target, classification, Ct);

        public async Task<string> Snapshot(string participantId)
        {
            using var fresh = EntityContext.NoCache();
            return JsonSerializer.Serialize(new
            {
                Participant = await Participant.Get(participantId, Ct),
                Site = await TangentSite.Get(TangentConstants.SiteId, Ct),
                JournalHead = await ActivityHead.Get(ActivityHead.Key, Ct)
            });
        }

        public void Dispose()
        {
            if (ReferenceEquals(AppHost.Current, host.Services)) AppHost.Current = null;
            host.Dispose();
            gate.Dispose();
            TestHooks.ResetDataConfigs();
            timeout.Dispose();
            // Only the GUID directory created by this fixture, never a supplied path.
            Directory.Delete(root, recursive: true);
        }

        private sealed class NoHandles : IAtprotoHandleSource
        {
            public Task<string?> HandleOf(string did, CancellationToken ct) => throw new InvalidOperationException("This fixture must not resolve remote handles.");
        }
    }
}
