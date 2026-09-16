namespace TangentSpace.Conversation;

public sealed partial class ConversationService
{
    private readonly ConversationUpdates updates = new();

    public async Task<PostPage> WaitForUpdates(string did, string topic, string cursor, CancellationToken ct)
    {
        // Denied and unknown topics never allocate notification state.
        await ReadPolicy(did, topic, ct);
        if (string.IsNullOrEmpty(cursor)) throw new ArgumentException("Supply a resume cursor returned with a history page.");
        var selected = Decode(cursor, did, topic);
        if (selected.Boundary is not null) throw new ArgumentException("Wait for updates with a resume cursor, not a paging continuation.");

        // Capture before the history read: a commit between the read and wait must remain observable.
        using var notification = updates.Capture(topic);
        var page = await History(did, topic, cursor, ct);
        if (page.Messages.Count > 0 || page.Boundary > selected.After) return page;

        await notification.WaitAsync(TimeSpan.FromSeconds(15), ct);
        // No policy gate is held during the wait, and removal/suspension is checked again here.
        return await History(did, topic, cursor, ct);
    }
}
