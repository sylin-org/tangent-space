using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Tangent.Space;

namespace Tangent.Lifecycle;

/// <summary>A backup is the data folder, consistent, copied: the database through SQLite's <c>VACUUM INTO</c>
/// and every other file as it is, in a folder of its own with a manifest that names each file, its length and
/// its SHA-256, and the versions that wrote it. Restoring is putting that folder back where the data folder
/// goes. Nothing here deletes a data folder: a folder that is replaced is moved aside with a timestamp.</summary>
public static class Backup
{
    public const string ManifestFile = "backup.json";

    public const string Format = "tangent-backup-1";

    private const string FolderPrefix = "tangent-backup-";

    private const string Stamp = "yyyyMMdd'T'HHmmss'Z'";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The database and the files SQLite keeps beside it, whose content leaves through <c>VACUUM INTO</c>,
    /// and the file that belongs to the running process: its mark.</summary>
    private static readonly string[] NotCopied =
    [
        TangentSpaceOptions.DatabaseFile,
        TangentSpaceOptions.DatabaseFile + "-journal",
        TangentSpaceOptions.DatabaseFile + "-wal",
        TangentSpaceOptions.DatabaseFile + "-shm",
        RunningMark.FileName
    ];

    /// <summary>How often a file another process holds is tried before the backup reports it, and how long apart.</summary>
    private const int HeldAttempts = 5;

    private static readonly TimeSpan HeldPause = TimeSpan.FromMilliseconds(100);

    /// <summary>Backs up <paramref name="dataFolder"/> into a new timestamped folder under
    /// <paramref name="destination"/> and returns its path. Safe while the Tangent Space runs: the database is
    /// read through one connection whose <c>VACUUM INTO</c> sees a single transaction. The connection may write,
    /// so SQLite rolls back a journal an unclean stop left behind before it reads. A failed backup leaves nothing.</summary>
    public static string Take(string dataFolder, string destination)
    {
        dataFolder = FullPath(dataFolder);
        destination = FullPath(destination);
        if (!Directory.Exists(dataFolder))
            throw new LifecycleRefusal($"There is no data folder at {dataFolder} to back up.");
        if (IsWithin(destination, dataFolder))
            throw new LifecycleRefusal($"A backup is never written inside the data folder it copies: choose a folder outside {dataFolder}.");
        var files = OrdinaryFiles(dataFolder);

        Directory.CreateDirectory(destination);
        var createdAt = DateTimeOffset.UtcNow;
        var target = FreeName(destination, FolderPrefix + createdAt.ToString(Stamp));
        // Written under a name no restore accepts, and named as a backup only once it is whole.
        var partial = target + ".partial";
        Directory.CreateDirectory(partial);
        try
        {
            Write(dataFolder, files, partial, createdAt);
            Move(partial, target);
            return target;
        }
        catch
        {
            Directory.Delete(partial, recursive: true);
            throw;
        }
    }

    private static void Write(string dataFolder, List<string> files, string partial, DateTimeOffset createdAt)
    {
        string? sqliteVersion = null;
        var database = Path.Combine(dataFolder, TangentSpaceOptions.DatabaseFile);
        if (File.Exists(database))
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString());
            connection.Open();
            using (var vacuum = connection.CreateCommand())
            {
                vacuum.CommandText = "VACUUM INTO $target";
                vacuum.Parameters.AddWithValue("$target", Path.Combine(partial, TangentSpaceOptions.DatabaseFile));
                vacuum.ExecuteNonQuery();
            }
            using var version = connection.CreateCommand();
            version.CommandText = "select sqlite_version()";
            sqliteVersion = (string?)version.ExecuteScalar();
        }

        foreach (var relative in files.Where(relative => !NotCopied.Contains(relative, StringComparer.OrdinalIgnoreCase)))
            Copy(dataFolder, relative, partial);

        var manifest = new BackupManifest(
            Format,
            createdAt,
            Path.GetFileName(dataFolder),
            new BackupVersions(
                VersionOf(typeof(Backup).Assembly),
                VersionOf(typeof(Koan.Core.KoanEnv).Assembly),
                sqliteVersion,
                Environment.Version.ToString()),
            OrdinaryFiles(partial).Select(relative => Describe(partial, relative)).ToList());
        File.WriteAllText(Path.Combine(partial, ManifestFile), JsonSerializer.Serialize(manifest, Json));
    }

    /// <summary>Copies one file of a live data folder as it is. A file gone since the folder was listed — a key the
    /// running process wrote through a temporary file it then renamed — is left out, and so is absent from the
    /// manifest, which names what the backup holds. A file another process holds is tried again briefly and then
    /// reported by name.</summary>
    private static void Copy(string dataFolder, string relative, string partial)
    {
        for (var attempt = 1; ; attempt++)
        {
            FileStream source;
            try
            {
                source = new FileStream(Path.Combine(dataFolder, relative), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (Exception gone) when (gone is FileNotFoundException or DirectoryNotFoundException)
            {
                return;
            }
            catch (IOException held)
            {
                if (attempt == HeldAttempts)
                    throw new LifecycleRefusal($"{relative} in the data folder is held by another process and could not be copied: {held.Message}");
                Thread.Sleep(HeldPause);
                continue;
            }

            using (source)
            {
                var copy = Path.Combine(partial, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                using var written = new FileStream(copy, FileMode.CreateNew, FileAccess.Write);
                source.CopyTo(written);
            }
            return;
        }
    }

    /// <summary>Puts the backup at <paramref name="backupFolder"/> into <paramref name="dataFolder"/>. An absent or
    /// empty data folder receives it; a non-empty one is refused unless <paramref name="replace"/> is set, which
    /// moves it aside first. With no backup folder, <paramref name="replace"/> only moves the data folder aside, so
    /// the next start begins a new Tangent Space. The backup is verified against its manifest before anything
    /// moves.</summary>
    public static RestoreResult Restore(string? backupFolder, string dataFolder, bool replace)
    {
        dataFolder = FullPath(dataFolder);
        backupFolder = backupFolder is null ? null : FullPath(backupFolder);
        var mark = RunningMark.Inspect(dataFolder);
        if (mark.InUse)
            throw new LifecycleRefusal(
                $"The data folder {dataFolder} is in use by a running Tangent Space ({mark.Holder}). Stop it before restoring.");
        var manifest = backupFolder is null ? null : Verify(backupFolder, dataFolder);
        var occupied = Directory.Exists(dataFolder) && Directory.EnumerateFileSystemEntries(dataFolder).Any();
        if (occupied && !replace)
            throw new LifecycleRefusal(
                $"The data folder {dataFolder} is not empty. Restore puts a backup into an empty or absent folder; " +
                "pass --replace to move the current folder aside first.");

        var stamp = DateTimeOffset.UtcNow.ToString(Stamp);
        var parent = Path.GetDirectoryName(dataFolder)
            ?? throw new LifecycleRefusal($"The data folder {dataFolder} is a filesystem root; it needs a folder of its own.");
        Directory.CreateDirectory(parent);

        string? staged = null;
        string? movedAside = null;
        try
        {
            if (manifest is not null)
            {
                staged = FreeName(parent, Path.GetFileName(dataFolder) + "-restoring-" + stamp);
                foreach (var file in manifest.Files)
                {
                    var copy = Path.Combine(staged, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                    File.Copy(Path.Combine(backupFolder!, file.Path), copy);
                }
            }

            if (occupied)
            {
                var aside = FreeName(parent, Path.GetFileName(dataFolder) + "-before-restore-" + stamp);
                Move(dataFolder, aside);
                movedAside = aside;
            }
            else if (Directory.Exists(dataFolder) && staged is not null)
            {
                Directory.Delete(dataFolder); // empty: the move below needs its name
            }

            if (staged is not null) Move(staged, dataFolder);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new LifecycleRefusal($"Restore failed: {failure.Message} {PutBack(dataFolder, movedAside, staged)}");
        }

        return new RestoreResult(dataFolder, manifest?.Files.Count ?? 0, movedAside, mark.Stale ? mark.Holder : null);
    }

    /// <summary>After a failed restore: the copy in progress is removed, and a data folder already moved aside goes
    /// back where it was. Says where the data folder is.</summary>
    private static string PutBack(string dataFolder, string? movedAside, string? staged)
    {
        if (staged is not null && Directory.Exists(staged))
        {
            try { Directory.Delete(staged, recursive: true); }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
        }

        if (movedAside is null) return "Nothing was moved aside.";
        if (Directory.Exists(dataFolder))
            return $"The data folder that was moved aside is at {movedAside}.";
        try
        {
            Move(movedAside, dataFolder);
            return $"The data folder was moved back from {movedAside} to {dataFolder}.";
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return $"The data folder is at {movedAside}; move it back to {dataFolder} before starting.";
        }
    }

    /// <summary>Reads the manifest and checks every file it names: contained in the backup, present, of the stated
    /// length and SHA-256.</summary>
    private static BackupManifest Verify(string backupFolder, string dataFolder)
    {
        var manifestPath = Path.Combine(backupFolder, ManifestFile);
        if (!File.Exists(manifestPath))
            throw new LifecycleRefusal($"{backupFolder} is not a backup: it has no {ManifestFile}.");
        if (IsWithin(backupFolder, dataFolder) || IsWithin(dataFolder, backupFolder))
            throw new LifecycleRefusal("A backup is restored from outside the data folder it fills.");

        BackupManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), Json); }
        catch (JsonException failure) { throw new LifecycleRefusal($"{manifestPath} is not readable: {failure.Message}"); }
        if (manifest?.Format != Format || manifest.Files is null)
            throw new LifecycleRefusal($"{manifestPath} is not a {Format} manifest.");

        foreach (var file in manifest.Files)
        {
            var path = Path.GetFullPath(Path.Combine(backupFolder, file.Path ?? ""));
            if (string.IsNullOrEmpty(file.Path) || Path.IsPathRooted(file.Path) || !IsWithin(path, backupFolder)
                || string.Equals(file.Path, ManifestFile, StringComparison.OrdinalIgnoreCase))
                throw new LifecycleRefusal($"The manifest names a file outside the backup: '{file.Path}'.");
            if (!File.Exists(path))
                throw new LifecycleRefusal($"The backup is missing {file.Path}.");
            var found = Describe(backupFolder, file.Path);
            if (found.Length != file.Length || !string.Equals(found.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new LifecycleRefusal($"{file.Path} differs from the manifest: the backup is damaged.");
        }

        return manifest;
    }

    /// <summary>Every file under <paramref name="folder"/> as a path relative to it. A link is refused, so a copy never
    /// follows one out of the folder or leaves its content behind.</summary>
    private static List<string> OrdinaryFiles(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        var files = new List<string>();
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
        {
            if (entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new LifecycleRefusal($"{entry.FullName} is a link; the data folder holds ordinary files and folders only.");
            if (entry is FileInfo) files.Add(Path.GetRelativePath(folder, entry.FullName));
        }

        return files;
    }

    private static BackupFile Describe(string folder, string relative)
    {
        using var stream = File.OpenRead(Path.Combine(folder, relative));
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        return new BackupFile(relative.Replace('\\', '/'), stream.Length, hash);
    }

    /// <summary><paramref name="name"/> under <paramref name="parent"/>, with a counter when that name is taken.</summary>
    /// <summary>How many times a backup's or a restore's folder move is tried, and the first pause between tries, which doubles up
    /// to a second: about five seconds in all.</summary>
    private const int MoveAttempts = 8;
    private static readonly TimeSpan MovePause = TimeSpan.FromMilliseconds(100);

    /// <summary>Moves a folder, trying again for a moment when Windows still holds a file in it (an indexer or a virus
    /// scanner reading what was just written) or has not yet let go of the folder just deleted under the name it moves
    /// to; the last failure is the one thrown.</summary>
    private static void Move(string from, string to)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (Exception failure) when (attempt < MoveAttempts && Directory.Exists(from)
                && failure is UnauthorizedAccessException or IOException { HResult: SharingViolation or LockViolation or AccessDenied })
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(1000, MovePause.TotalMilliseconds * (1 << (attempt - 1)))));
            }
        }
    }

    /// <summary>What Directory.Move reports, as an <see cref="IOException"/> rather than an
    /// <see cref="UnauthorizedAccessException"/>, when another process briefly holds a file inside the folder.</summary>
    private const int AccessDenied = unchecked((int)0x80070005);

    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    private static string FreeName(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        for (var counter = 2; Directory.Exists(path) || File.Exists(path) || Directory.Exists(path + ".partial"); counter++)
            path = Path.Combine(parent, $"{name}-{counter}");
        return path;
    }

    private static string FullPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsWithin(string path, string folder)
    {
        var relative = Path.GetRelativePath(folder, path);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    private static string VersionOf(Assembly assembly)
        => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
}

/// <summary>What <c>backup.json</c> records: its format, when the backup was taken, the data folder's name, the
/// versions that wrote it and every file with its length and SHA-256.</summary>
public sealed record BackupManifest(
    string Format,
    DateTimeOffset CreatedAt,
    string DataFolder,
    BackupVersions Versions,
    IReadOnlyList<BackupFile> Files);

public sealed record BackupVersions(string Tangent, string Koan, string? Sqlite, string Runtime);

public sealed record BackupFile(string Path, long Length, string Sha256);

/// <summary>What a restore did: the files it put back, where the replaced folder went, and the process named by a
/// stale running mark it ignored.</summary>
public sealed record RestoreResult(string DataFolder, int Files, string? MovedAside, string? StaleMark);

/// <summary>A lifecycle verb declined to act, with the reason an operator reads.</summary>
public sealed class LifecycleRefusal(string message) : Exception(message);
