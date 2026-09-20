using Koan.Data.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tangent.Activity;

namespace Tangent.Infrastructure;

/// <summary>
/// In-memory-primary data persistence: the entity store lives in a shared in-memory
/// SQLite database (every read and write is a memory operation), and this service
/// offloads it to the durable snapshot on disk in the background — at a fixed cadence
/// when the journal moved, and once more on shutdown. At startup the last snapshot is
/// restored into memory before any projection job or request touches the store.
/// </summary>
public sealed class MemoryDataPersistence(
    ILogger<MemoryDataPersistence> logger,
    IConfiguration configuration) : BackgroundService
{
    private static readonly TimeSpan SnapshotCadence = TimeSpan.FromSeconds(5);

    private SqliteConnection? _keeper;
    private SqliteConnection? _snapshotConnection;
    private long _snapshottedThrough = -1;
    private readonly TaskCompletionSource _restored = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string MemoryConnectionString =>
        configuration["Tangent:Data:MemoryConnectionString"] ?? "Data Source=file:koanmem?mode=memory&cache=shared";
    private string SnapshotPath => configuration["Tangent:Data:SnapshotPath"] ?? "/state/tangent.sqlite";
    private bool Enabled => configuration["Tangent:Data:MemoryPrimary"] == "true";

    /// <summary>Projection jobs await this: compiling before the snapshot is restored
    /// would compute against an empty store and be overwritten by it.</summary>
    public Task WaitForRestoreAsync(CancellationToken ct) => _restored.Task.WaitAsync(ct);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!Enabled)
        {
            logger.LogInformation("In-memory data persistence is not enabled; the entity store stays on disk.");
            _restored.TrySetResult();
            return;
        }

        _keeper = new SqliteConnection(MemoryConnectionString);
        _keeper.Open();
        // The keeper holds the shared in-memory database alive for the process lifetime.

        await RestoreAsync(ct);
        _restored.TrySetResult();

        using var timer = new PeriodicTimer(SnapshotCadence);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var latest = await ActivityJournal.LatestSequence(ct);
                if (latest != _snapshottedThrough) await SnapshotAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown: the final snapshot happens in StopAsync.
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        if (Enabled) await SnapshotAsync(CancellationToken.None);
        await base.StopAsync(ct);
    }

    private async Task RestoreAsync(CancellationToken ct)
    {
        if (!File.Exists(SnapshotPath))
        {
            logger.LogInformation("No entity snapshot at {path}; the in-memory store starts empty.", SnapshotPath);
            _snapshottedThrough = await ActivityJournal.LatestSequence(ct);
            _restored.TrySetResult();
            return;
        }
        using var disk = new SqliteConnection("Data Source=" + SnapshotPath);
        disk.Open();
        // disk -> memory: the online backup copies a consistent image while the store
        // has no other connections yet; the keeper keeps the memory side alive.
        _keeper!.BackupDatabase(disk);
        _snapshottedThrough = await ActivityJournal.LatestSequence(ct);
        logger.LogInformation("Restored the entity snapshot from {path} into the in-memory store.", SnapshotPath);
        _restored.TrySetResult();
    }

    private async Task SnapshotAsync(CancellationToken ct)
    {
        if (_keeper is null) return;
        try
        {
            if (_snapshotConnection is null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
                _snapshotConnection = new SqliteConnection("Data Source=" + SnapshotPath);
                _snapshotConnection.Open();
            }
            // memory -> disk: the online backup copies a consistent image while readers
            // and writers continue on the memory database.
            _keeper.BackupDatabase(_snapshotConnection);
            _snapshottedThrough = await ActivityJournal.LatestSequence(ct);
            logger.LogInformation("Entity snapshot written to {path}.", SnapshotPath);
        }
        catch (Exception error)
        {
            logger.LogError(error, "The entity snapshot to {path} failed; the previous snapshot remains.", SnapshotPath);
        }
    }
}
