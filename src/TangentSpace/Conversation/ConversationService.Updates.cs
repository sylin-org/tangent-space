namespace TangentSpace.Conversation;

public sealed partial class ConversationService
{
    private readonly ConversationUpdates updates = new();

    public async Task<MessagePage> WaitForUpdates(string did, string room, string cursor, CancellationToken ct)
    {
        // Denied and unknown rooms never allocate notification state.
        await ReadPolicy(did, room, ct);
        if (string.IsNullOrEmpty(cursor)) throw new ArgumentException("Supply a resume cursor returned with a history page.");
        var selected = Decode(cursor, did, room);
        if (selected.Boundary is not null) throw new ArgumentException("Wait for updates with a resume cursor, not a paging continuation.");

        // Capture before the history read: a commit between the read and wait must remain observable.
        using var notification = updates.Capture(room);
        var page = await History(did, room, cursor, ct);
        if (page.Messages.Count > 0 || page.Boundary > selected.After) return page;

        await notification.WaitAsync(TimeSpan.FromSeconds(15), ct);
        // No policy gate is held during the wait, and removal/suspension is checked again here.
        return await History(did, room, cursor, ct);
    }
}
