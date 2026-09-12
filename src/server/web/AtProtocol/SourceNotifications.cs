using System.Threading.Channels;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.Extensions.Options;
using TangentSpace.Conversation;
using TangentSpace.Infrastructure;
using TangentSpace.Rooms;

namespace TangentSpace.AtProtocol;

/// <summary>One durable inbox and due-work loop for native source hints, renewal and bounded repair.</summary>
public sealed class SourceNotifications(PolicyGate gate, SpacesService spaces, ConversationService conversation,
    IOptions<SpacesOptions> options, TimeProvider clock, ILogger<SourceNotifications> logger)
{
    private static readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false });
    private int maintenancePage = 1;
    private int repairPage = 1;
    private DateTimeOffset nextMaintenance;
    private DateTimeOffset nextRepair;
    private static int maintenanceRequested;

    public static void Signal() => wake.Writer.TryWrite(true);
    public static void RequestMaintenance() { Interlocked.Exchange(ref maintenanceRequested, 1); Signal(); }

    public async Task<bool> Enqueue(SourceNotificationRequest request, CancellationToken ct)
    {
        if (!request.TryValidate(options.Value, out var roomKey, out var hash)) throw new ArgumentException("Invalid Space notification.");
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var room = await Room.Get(roomKey, ct);
            if (room is null || room.SpaceState != RoomSpaceState.Ready || room.SpaceUri != request.Space) return false;
            var key = SourceNotification.Key(roomKey, request.Repo);
            var work = await SourceNotification.Get(key, ct);
            if (work is not null && work.Revision == request.Rev && work.CommitHash == hash)
            { await EntityContext.Commit(ct); return true; }
            if (work is null)
            {
                var pending = await SourceNotification.Query(x => x.Pending, new QueryDefinition { Page = 1, PageSize = 2048 }, ct);
                if (pending.Count >= 2048) throw new InvalidOperationException("Source inbox is at its bounded capacity.");
                work = new SourceNotification { Id = key, RoomKey = roomKey, Space = request.Space, RepoDid = request.Repo };
            }
            work.Revision = request.Rev; work.CommitHash = hash;
            work.Generation = checked(work.Generation + 1); work.Pending = true; work.Attempts = 0;
            work.ReceivedAt = clock.GetUtcNow(); work.NextAttemptAt = work.ReceivedAt; work.Status = "pending";
            await work.Save(ct);
            await EntityContext.Commit(ct);
        }
        finally { gate.Exit(); }
        Signal();
        return true;
    }

    public async Task Run(CancellationToken ct)
    {
        nextRepair = clock.GetUtcNow().AddMinutes(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var fresh = EntityContext.NoCache();
                var now = clock.GetUtcNow();
                var work = await SourceNotification.Query(x => x.Pending && x.NextAttemptAt <= now,
                    new QueryDefinition { Page = 1, PageSize = 8 }, ct);
                foreach (var item in work) await Process(item, ct);
                if (work.Count == 8) { await Task.Yield(); continue; }
                if (Interlocked.Exchange(ref maintenanceRequested, 0) != 0) { maintenancePage = 1; nextMaintenance = default; }
                if (now >= nextMaintenance)
                {
                    await Maintain(ct);
                    await conversation.ReconcilePending(ct);
                    nextMaintenance = clock.GetUtcNow().AddMinutes(1);
                }
                if (now >= nextRepair)
                {
                    var rooms = await Room.Query(x => x.SpaceState == RoomSpaceState.Ready,
                        new QueryDefinition { Page = repairPage, PageSize = 2 }, ct);
                    foreach (var room in rooms) await conversation.Reconcile(null, room.Id, ct);
                    repairPage = rooms.Count < 2 ? 1 : repairPage + 1;
                    nextRepair = clock.GetUtcNow().AddMinutes(5);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning("Source due-work will retry: {FailureType}", error.GetType().Name);
            }
            // This is one bounded repair/backoff timer, not a room or model polling loop.
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(30));
            try { await wake.Reader.ReadAsync(wait.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
    }

    private async Task Process(SourceNotification snapshot, CancellationToken ct)
    {
        var status = "checked";
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(45));
            var pendingReplies = await conversation.ReconcileWriter(snapshot.RoomKey, snapshot.RepoDid, budget.Token);
            if (pendingReplies != 0) status = "waiting-for-reply-source";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is SpacesUnavailable or HttpRequestException or InvalidDataException
            or System.Text.Json.JsonException or InvalidOperationException or OperationCanceledException)
        {
            status = ConversationRecovery.RequiresReauthorization(error) ? "authority-reauthorization-required" : "source-unavailable";
            logger.LogDebug("Native source notification deferred for {Room}: {FailureType}", snapshot.RoomKey, error.GetType().Name);
        }
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var current = await SourceNotification.Get(snapshot.Id, ct);
            if (current is not null && current.Generation == snapshot.Generation)
            {
                current.Status = status;
                current.Pending = status != "checked";
                current.Attempts = Math.Min(current.Attempts + 1, 20);
                current.NextAttemptAt = clock.GetUtcNow().AddSeconds(Math.Min(900, 5 * Math.Pow(2, Math.Min(8, current.Attempts))));
                if (!current.Pending) current.ProcessedAt = clock.GetUtcNow();
                await current.Save(ct);
            }
            await EntityContext.Commit(ct);
        }
        finally { gate.Exit(); }
    }

    private async Task Maintain(CancellationToken ct)
    {
        var rooms = await Room.Query(x => x.SpaceState == RoomSpaceState.Ready,
            new QueryDefinition { Page = maintenancePage, PageSize = 8 }, ct);
        foreach (var room in rooms)
        {
            var current = await SourceSubscription.Get(room.Id, ct);
            var now = clock.GetUtcNow();
            if (current is not null && current.Space == room.SpaceUri && current.Service == options.Value.NotificationService
                && current.ExpiresAt > now.AddMinutes(10) && current.Status == "subscribed") continue;
            if (current?.NextAttemptAt > now) continue;
            try
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                budget.CancelAfter(TimeSpan.FromSeconds(20));
                var expires = await spaces.RegisterNotifications(room.SpaceUri!, budget.Token);
                await SaveSubscription(room, expires, "subscribed", ct);
                logger.LogInformation("Native Space notifications subscribed for {Room} until {Expiry}", room.Id, expires);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is SpacesUnavailable or HttpRequestException or InvalidDataException
                or System.Text.Json.JsonException or InvalidOperationException or OperationCanceledException)
            {
                await SaveSubscription(room, DateTimeOffset.MinValue, "registration-unavailable", ct);
                logger.LogWarning("Native Space subscription will retry for {Room}: {FailureType}", room.Id, error.GetType().Name);
            }
        }
        maintenancePage = rooms.Count < 8 ? 1 : maintenancePage + 1;
    }

    private async Task SaveSubscription(Room room, DateTimeOffset expires, string status, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            await new SourceSubscription { Id = room.Id, Space = room.SpaceUri!, Service = options.Value.NotificationService,
                ExpiresAt = expires, Status = status, NextAttemptAt = clock.GetUtcNow().AddMinutes(1) }.Save(ct);
            await EntityContext.Commit(ct);
        }
        finally { gate.Exit(); }
    }
}
