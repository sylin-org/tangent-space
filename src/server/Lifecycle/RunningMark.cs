using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Tangent.Space;

namespace Tangent.Lifecycle;

/// <summary>Marks the data folder as in use for as long as the Tangent Space runs: <c>tangent.running</c> holds the
/// process id and start time and stays open under a lock, so a restore refuses the folder from this process, another
/// process or another container on the same volume. A clean stop removes it; after a crash the lock is gone and the
/// mark is stale.</summary>
public sealed class RunningMark(IOptions<TangentSpaceOptions> options, IHostEnvironment environment) : IHostedService
{
    public const string FileName = "tangent.running";

    private FileStream? _held;
    private string? _path;

    public Task StartAsync(CancellationToken ct)
    {
        var folder = options.Value.DataFolderPath(environment.ContentRootPath);
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, FileName);
        // Shared with readers, so a restore can name the process; never with another writer.
        _held = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var current = Process.GetCurrentProcess();
        _held.Write(Encoding.ASCII.GetBytes($"{current.Id} {current.StartTime.ToUniversalTime().Ticks}"));
        _held.Flush(flushToDisk: true);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        if (_held is null) return Task.CompletedTask;
        _held.Dispose();
        _held = null;
        File.Delete(_path!);
        return Task.CompletedTask;
    }

    /// <summary>Whether a running Tangent Space holds <paramref name="dataFolder"/>: the mark is locked, or names a
    /// process that exists with that start time. A mark that does neither is stale.</summary>
    public static MarkState Inspect(string dataFolder)
    {
        var path = Path.Combine(dataFolder, FileName);
        if (!File.Exists(path)) return new MarkState(false, null);
        string? holder;
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            holder = new StreamReader(probe, Encoding.ASCII).ReadToEnd();
        }
        catch (IOException)
        {
            return new MarkState(true, Describe(ReadShared(path)));
        }

        return new MarkState(IsAlive(holder), Describe(holder));
    }

    private static string? ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new StreamReader(stream, Encoding.ASCII).ReadToEnd();
        }
        catch (IOException) { return null; }
    }

    private static bool IsAlive(string? holder)
    {
        if (!TryParse(holder, out var id, out var started)) return false;
        try
        {
            using var process = Process.GetProcessById(id);
            return process.StartTime.ToUniversalTime().Ticks == started;
        }
        catch (Exception missing) when (missing is ArgumentException or InvalidOperationException) { return false; }
    }

    private static string Describe(string? holder) => TryParse(holder, out var id, out var started)
        ? $"process {id}, started {new DateTime(started, DateTimeKind.Utc):u}"
        : "a process that could not be named";

    private static bool TryParse(string? holder, out int id, out long started)
    {
        id = 0;
        started = 0;
        var parts = holder?.Trim().Split(' ');
        return parts is { Length: 2 } && int.TryParse(parts[0], out id) && long.TryParse(parts[1], out started);
    }
}

/// <summary>What a data folder's mark says: whether a running Tangent Space holds it, and which process wrote it.</summary>
public sealed record MarkState(bool InUse, string? Holder)
{
    public bool Stale => !InUse && Holder is not null;
}
