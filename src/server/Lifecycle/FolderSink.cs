namespace Tangent.Lifecycle;

/// <summary>A folder written as it is made, under a name beside it that nothing serves, and given its own name only once
/// it is whole (<see cref="Finish"/>), so a walk that fails leaves nothing that reads as complete. It takes an empty or
/// absent folder and nothing else, so it never mixes with what a folder held. It knows nothing of what its files
/// are.</summary>
public sealed class FolderSink : IFileSink
{
    private readonly string _folder;
    private readonly string _partial;

    private FolderSink(string folder)
    {
        _folder = folder;
        _partial = folder + ".partial";
    }

    /// <summary>A sink for <paramref name="folder"/>, refused where it holds anything. What an earlier walk that never
    /// finished left beside it goes.</summary>
    public static FolderSink Open(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            throw new LifecycleRefusal($"{full} is not empty; an archive goes into an empty or absent folder.");
        var sink = new FolderSink(full);
        sink.Abandon();
        Directory.CreateDirectory(sink._partial);
        return sink;
    }

    public async Task Write(string path, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        await using var file = Created(path);
        await file.WriteAsync(content, ct);
    }

    public async Task Copy(string path, Stream source, CancellationToken ct)
    {
        await using var file = Created(path);
        await source.CopyToAsync(file, ct);
    }

    /// <summary>Gives the folder its name.</summary>
    public void Finish()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder); // empty: the move needs its name
        Backup.Move(_partial, _folder);
    }

    /// <summary>Removes what was written.</summary>
    public void Abandon()
    {
        if (Directory.Exists(_partial)) Directory.Delete(_partial, recursive: true);
    }

    private FileStream Created(string path)
    {
        var target = Path.Combine(_partial, path);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        return File.Create(target);
    }
}
