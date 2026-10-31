using System.IO.Compression;

namespace Tangent.Lifecycle;

/// <summary>A zip written onto a stream as it is made: each file goes out when it is written and nothing of it stays
/// here, so however many files it holds, it holds only the one being written. The stream need not seek, so it may be
/// a response. Only <see cref="Finish"/> writes the zip's directory: one left unfinished never opens as complete. It
/// knows nothing of what its files are.</summary>
public sealed class ZipSink : IFileSink
{
    private readonly ZipArchive _zip;

    private ZipSink(ZipArchive zip) => _zip = zip;

    /// <summary>A zip onto <paramref name="output"/>, which stays open.</summary>
    public static async Task<ZipSink> Open(Stream output, CancellationToken ct)
        => new(await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, ct));

    /// <summary>A file of <paramref name="content"/>, compressed.</summary>
    public async Task Write(string path, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        await using var file = await _zip.CreateEntry(path, CompressionLevel.Optimal).OpenAsync(ct);
        await file.WriteAsync(content, ct);
    }

    /// <summary>A file read from <paramref name="source"/> as it goes, kept as it is: what a sink copies is compressed
    /// already, or small.</summary>
    public async Task Copy(string path, Stream source, CancellationToken ct)
    {
        await using var file = await _zip.CreateEntry(path, CompressionLevel.NoCompression).OpenAsync(ct);
        await source.CopyToAsync(file, ct);
    }

    /// <summary>Writes the directory that makes the zip whole.</summary>
    public ValueTask Finish() => _zip.DisposeAsync();
}
