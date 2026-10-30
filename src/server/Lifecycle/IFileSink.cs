namespace Tangent.Lifecycle;

/// <summary>Where a walk of the place writes its files as it reads them, each by its path from the root, so however
/// many it writes it holds only the one being written.</summary>
public interface IFileSink
{
    /// <summary>A file of <paramref name="content"/>.</summary>
    Task Write(string path, ReadOnlyMemory<byte> content, CancellationToken ct);

    /// <summary>A file read from <paramref name="source"/> as it goes.</summary>
    Task Copy(string path, Stream source, CancellationToken ct);
}
