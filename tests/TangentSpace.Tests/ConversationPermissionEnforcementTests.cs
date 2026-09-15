using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using TangentSpace.Activity;
using TangentSpace.Conversation;
using TangentSpace.Rooms;
using TangentSpace.Tests.ExperienceIntegration;
using Xunit;

namespace TangentSpace.Tests;

/// <summary>Post authorization through the real conversation/governance services and SQLite,
/// including receipt, source, live-row, changelog and journal effects. Seeded source posts are
/// not used for mutations: each test first creates its own post through the local write path.</summary>
[Collection("Experience integration")]
public sealed class ConversationPermissionEnforcementTests : IAsyncLifetime
{
    private ExperienceWebApp app = null!;
    private ConversationService Conversation => app.Services.GetRequiredService<ConversationService>();
    private RoomGovernance Rooms => app.Services.GetRequiredService<RoomGovernance>();
    private const string Topic = ExperienceWebApp.TopicKey;

    public async ValueTask InitializeAsync()
    {
        app = await ExperienceWebApp.StartAsync();
        await Settings(locked: false);
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    [Fact]
    public async Task Reader_cannot_create_edit_or_delete_and_denials_leave_no_receipts_or_history()
    {
        var post = await CreatePost();
        await Assign(ExperienceWebApp.AgentDid, RoomRole.Reader);
        var policy = await Policy(app.AgentParticipantId);
        Assert.True(policy.CanRead);
        Assert.False(policy.CanWrite);
        var before = await Capture();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Conversation.Post(app.AgentParticipantId, Topic,
            new PostMessage("reader-create", "This must never be accepted."), CancellationToken.None));
        Assert.Equal(before, await Capture());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Change(app.AgentParticipantId, post.Id,
            "reader-edit", delete: false, text: "This must never replace the author's words."));
        Assert.Equal(before, await Capture());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Change(app.AgentParticipantId, post.Id,
            "reader-delete", delete: true));
        Assert.Equal(before, await Capture());
    }

    [Fact]
    public async Task Moderator_can_remove_another_authors_post_but_cannot_rewrite_it()
    {
        var post = await CreatePost();
        await Assign(ExperienceWebApp.HumanDid, RoomRole.Manager);
        var before = await Capture();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Change(app.HumanParticipantId, post.Id,
            "moderator-edit", delete: false, text: "A moderator's replacement words."));
        Assert.Equal(before, await Capture());

        await Settings(locked: true);
        var result = await Change(app.HumanParticipantId, post.Id, "moderator-remove", delete: true);
        Assert.Equal("moderated", result.State);
        using var fresh = EntityContext.NoCache();
        var live = await Message.Get(post.Id);
        Assert.NotNull(live);
        Assert.True(live.Removed);
        Assert.Equal("", live.Content.Text);
        Assert.Equal(post.AuthorParticipantId, live.AuthorParticipantId);
        Assert.Equal(app.HumanParticipantId, live.RemovedByParticipantId);
        var snapshot = Assert.Single(await Snapshots(post.Id));
        Assert.Equal(post.Content, snapshot.Content);
        Assert.Equal(post.AuthorParticipantId, snapshot.AuthorParticipantId);
        Assert.Equal(snapshot.Id, live.ChangeId);
        Assert.False(snapshot.Removed);
        var receipt = Assert.Single(await PostChange.All());
        Assert.Equal("moderated", receipt.State);
        var deletion = Assert.Single((await ActivityJournal.All()).Where(entry => entry.Kind == ActivityKind.MessageDeleted));
        Assert.Equal(app.HumanParticipantId, deletion.ActorParticipantId);
        Assert.Equal(post.Sequence, deletion.MessageSequence);
    }

    [Fact]
    public async Task Removed_post_rejects_new_author_edit_delete_and_moderator_remove_without_duplicate_effects()
    {
        var post = await CreatePost();
        await Assign(ExperienceWebApp.HumanDid, RoomRole.Manager);
        await Change(app.HumanParticipantId, post.Id, "first-removal", delete: true);
        var before = await Capture();

        await Denied(() => Change(app.AgentParticipantId, post.Id, "removed-edit", delete: false, text: "Resurrected words."));
        Assert.Equal(before, await Capture());
        await Denied(() => Change(app.AgentParticipantId, post.Id, "removed-delete", delete: true));
        Assert.Equal(before, await Capture());
        await Denied(() => Change(app.HumanParticipantId, post.Id, "removed-moderate", delete: true));
        Assert.Equal(before, await Capture());
        Assert.Single(await Snapshots(post.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_delete_or_moderation_replays_after_topic_lock_without_another_snapshot(bool moderation)
    {
        var post = await CreatePost();
        if (moderation) await Assign(ExperienceWebApp.HumanDid, RoomRole.Manager);
        var actor = moderation ? app.HumanParticipantId : app.AgentParticipantId;
        var result = await Change(actor, post.Id, "completed-removal", delete: true);
        Assert.Equal(moderation ? "moderated" : "deleted", result.State);
        await Settings(locked: true);
        var policy = await Policy(actor);
        Assert.True(policy.CanRead);
        Assert.True(policy.Locked);
        Assert.False(policy.CanWrite);
        var before = await Capture();

        Assert.Equal(result, await Change(actor, post.Id, "completed-removal", delete: true));
        Assert.Equal(before, await Capture());
        Assert.Single(await Snapshots(post.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Management_or_ownership_without_read_access_cannot_remove_a_post(bool owner)
    {
        var post = await CreatePost();
        if (!owner) await Assign(ExperienceWebApp.HumanDid, RoomRole.Manager);
        var actor = owner ? app.OwnerParticipantId : app.HumanParticipantId;
        await MakeTopicUnreadable();
        var policy = await Policy(actor);
        Assert.True(policy.CanManage);
        Assert.Equal(owner, policy.IsOwner);
        Assert.False(policy.CanRead);
        var before = await Capture();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Change(actor, post.Id, "unreadable-removal", delete: true));
        Assert.Equal(before, await Capture());
    }

    [Fact]
    public async Task Completed_moderation_receipt_survives_demotion_but_not_read_revocation()
    {
        var post = await CreatePost();
        await Assign(ExperienceWebApp.HumanDid, RoomRole.Manager);
        var result = await Change(app.HumanParticipantId, post.Id, "recover-moderation", delete: true);
        await Assign(ExperienceWebApp.HumanDid, RoomRole.Reader);
        var policy = await Policy(app.HumanParticipantId);
        Assert.True(policy.CanRead);
        Assert.False(policy.CanManage);
        var before = await Capture();
        Assert.Equal(result, await Change(app.HumanParticipantId, post.Id, "recover-moderation", delete: true));
        Assert.Equal(before, await Capture());

        await Assign(ExperienceWebApp.HumanDid, RoomRole.Removed);
        Assert.False((await Policy(app.HumanParticipantId)).CanRead);
        before = await Capture();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Change(app.HumanParticipantId, post.Id, "recover-moderation", delete: true));
        Assert.Equal(before, await Capture());
        Assert.Single(await Snapshots(post.Id));
    }

    private async Task<Message> CreatePost()
    {
        var operation = "permission-post-" + Guid.CreateVersion7().ToString("N");
        var result = await Conversation.Post(app.AgentParticipantId, Topic,
            new PostMessage(operation, "Original words belong to their author."), CancellationToken.None);
        using var fresh = EntityContext.NoCache();
        var post = Assert.Single(await Message.Query(message => message.RoomKey == Topic
            && message.AuthorParticipantId == app.AgentParticipantId && message.OperationId == operation));
        Assert.Equal(result.Id, post.Id);
        Assert.StartsWith("local://", post.SourceUri);
        return post;
    }

    private Task<PostChangeResult> Change(string actor, string message, string operation, bool delete, string? text = null)
        => Conversation.ChangePost(actor, Topic, message, text, delete, operation, CancellationToken.None);

    private async Task Assign(string target, RoomRole role)
    {
        var result = await Rooms.SetMembership(app.OwnerParticipantId, Topic, target, role, CancellationToken.None);
        Assert.True(result.Accepted, result.Reason);
    }

    private async Task Settings(bool locked)
        => Assert.True((await Rooms.SetSettings(app.OwnerParticipantId, Topic, true, locked, null, null, CancellationToken.None)).Accepted);

    private Task<RoomPolicy> Policy(string actor)
        => Rooms.WithCurrentPolicy(actor, Topic, (policy, _) => Task.FromResult(policy), CancellationToken.None);

    // Humans lose conversation participation, reading included; management is authority, not participation.
    private Task<bool> MakeTopicUnreadable()
        => Rooms.WithCurrentPolicy(app.OwnerParticipantId, Topic, async (_, token) =>
        {
            var room = await Room.Get(Topic, token);
            Assert.NotNull(room);
            var tangent = await TangentSpace.Communities.TangentCommunity.Get(room.TangentKey, token);
            Assert.NotNull(tangent);
            tangent.ParticipationPreset = TangentSpace.Communities.ParticipationPreset.AgentsOnly;
            await tangent.Save(token);
            return true;
        }, CancellationToken.None);

    private static async Task Denied(Func<Task> operation)
    {
        var error = await Record.ExceptionAsync(operation);
        Assert.True(error is UnauthorizedAccessException or ArgumentException,
            $"Expected an authorization or invalid-post denial, got {error?.GetType().Name ?? "success"}.");
    }

    private static async Task<IReadOnlyList<Message>> Snapshots(string messageId)
    {
        using var fresh = EntityContext.NoCache();
        return (await Message.All(Message.ChangelogPartition)).Where(message => message.OfMessageId == messageId).ToArray();
    }

    private sealed record PersistedState(string Live, string History, string Sources, string Changes,
        string Conversation, string Journal, string Head, string Room, string Memberships, string Audits);

    private static async Task<PersistedState> Capture()
    {
        using var fresh = EntityContext.NoCache();
        // Tiny isolated fixture only: complete, sorted snapshots detect any unrequested row,
        // including moved messages and receipts that a Topic-filtered assertion could miss.
        return new PersistedState(
            JsonSerializer.Serialize((await Message.All()).OrderBy(row => row.Id)),
            JsonSerializer.Serialize((await Message.All(Message.ChangelogPartition)).OrderBy(row => row.Id)),
            JsonSerializer.Serialize((await SourceDecision.All()).OrderBy(row => row.Id)),
            JsonSerializer.Serialize((await PostChange.All()).OrderBy(row => row.Id)),
            JsonSerializer.Serialize(await RoomConversation.Get(Topic)),
            JsonSerializer.Serialize((await ActivityJournal.All()).OrderBy(row => row.Id)),
            JsonSerializer.Serialize(await ActivityHead.Get(ActivityHead.Key)),
            JsonSerializer.Serialize(await Room.Get(Topic)),
            JsonSerializer.Serialize((await RoomMembership.All()).OrderBy(row => row.Id)),
            JsonSerializer.Serialize((await RoomAudit.All()).OrderBy(row => row.Id)));
    }
}
