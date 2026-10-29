using System.Diagnostics;
using Koan.Data.Core;
using Microsoft.Data.Sqlite;
using Tangent.Space;
using Xunit;

namespace Tangent.Lifecycle;

/// <summary>The executable's <c>backup</c> and <c>restore</c> verbs, run as an operator runs them: the server
/// executable as its own process, pointed at a fixture's data folder.</summary>
public sealed class BackupTests(ITestOutputHelper output)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_backup_taken_while_it_runs_restores_into_a_fresh_folder_that_serves_the_same_row()
    {
        await using var original = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Koan:Web:ApplicationUrl"] = "https://backed-up.example" },
            ct: Cancellation);
        var row = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        row!.Name = "The workshop's place";
        row.Version++;
        await row.Save(Cancellation);
        var media = Path.Combine(original.DataFolder, "media", "cover.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(media)!);
        await File.WriteAllBytesAsync(media, [1, 2, 3], Cancellation);

        var backups = Path.Combine(original.Folder, "backups");
        await Server(0, "backup", backups, $"--Tangent:DataFolder={original.DataFolder}");
        await original.StopAsync(Cancellation);

        var backup = Assert.Single(Directory.GetDirectories(backups));
        Assert.True(File.Exists(Path.Combine(backup, Backup.ManifestFile)));
        var restoredFolder = Path.Combine(original.Folder, "restored");
        await Server(0, "restore", backup, $"--Tangent:DataFolder={restoredFolder}");

        await using var restored = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?>
            {
                ["Koan:Web:ApplicationUrl"] = "https://backed-up.example",
                ["Tangent:DataFolder"] = restoredFolder
            },
            ct: Cancellation);
        var read = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);

        Assert.Equal(restoredFolder, restored.DataFolder);
        Assert.Equal("The workshop's place", read?.Name);
        Assert.Equal("https://backed-up.example", read?.BaseUrl);
        Assert.Equal(row.Version, read?.Version);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(restoredFolder, "media", "cover.bin"), Cancellation));
    }

    [Fact]
    public async Task A_live_backup_leaves_out_the_connectors_session_lock_and_reports_a_file_another_process_holds()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var backups = Path.Combine(tangentSpace.Folder, "backups");
        var sessions = Directory.CreateDirectory(Path.Combine(tangentSpace.DataFolder, Participants.ParticipantsComposition.SessionFolder));
        var held = Path.Combine(tangentSpace.DataFolder, "held.txt");
        await File.WriteAllTextAsync(held, "held", Cancellation);

        using (new FileStream(Path.Combine(sessions.FullName, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            await Server(0, "backup", backups, $"--Tangent:DataFolder={tangentSpace.DataFolder}");
            using (new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var refused = await Server(1, "backup", backups, $"--Tangent:DataFolder={tangentSpace.DataFolder}");
                Assert.Contains("held.txt in the data folder is held by another process", refused);
            }
        }

        var backup = Assert.Single(Directory.GetDirectories(backups));
        Assert.True(File.Exists(Path.Combine(backup, "held.txt")));
        Assert.False(File.Exists(Path.Combine(backup, Participants.ParticipantsComposition.SessionFolder, "owner.lock")));
    }

    [Fact]
    public async Task Restore_refuses_a_folder_that_is_not_empty_and_with_replace_moves_it_aside()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var backups = Path.Combine(tangentSpace.Folder, "backups");
        await Server(0, "backup", backups, $"--Tangent:DataFolder={tangentSpace.DataFolder}");
        var backup = Assert.Single(Directory.GetDirectories(backups));
        var occupied = Path.Combine(tangentSpace.Folder, "occupied");
        Directory.CreateDirectory(occupied);
        await File.WriteAllTextAsync(Path.Combine(occupied, "kept.txt"), "kept", Cancellation);

        var refused = await Server(1, "restore", backup, $"--Tangent:DataFolder={occupied}");
        Assert.Contains("--replace", refused);
        Assert.Equal(["kept.txt"], Directory.GetFileSystemEntries(occupied).Select(Path.GetFileName));

        await Server(0, "restore", backup, "--replace", $"--Tangent:DataFolder={occupied}");
        var aside = Assert.Single(Directory.GetDirectories(tangentSpace.Folder, "occupied-before-restore-*"));
        Assert.Equal("kept", await File.ReadAllTextAsync(Path.Combine(aside, "kept.txt"), Cancellation));
        Assert.True(File.Exists(Path.Combine(occupied, TangentSpaceOptions.DatabaseFile)));
        Assert.False(File.Exists(Path.Combine(occupied, "kept.txt")));

        // Restoring nothing with --replace only moves the folder aside: the next start begins anew.
        await Server(0, "restore", "--replace", $"--Tangent:DataFolder={occupied}");
        Assert.False(Directory.Exists(occupied));
        Assert.Equal(2, Directory.GetDirectories(tangentSpace.Folder, "occupied-before-restore-*").Length);
    }

    [Fact]
    public async Task Restore_refuses_the_data_folder_of_a_running_tangent_space_and_proceeds_once_it_stops()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var backups = Path.Combine(tangentSpace.Folder, "backups");
        await Server(0, "backup", backups, $"--Tangent:DataFolder={tangentSpace.DataFolder}");
        var backup = Assert.Single(Directory.GetDirectories(backups));
        Assert.False(File.Exists(Path.Combine(backup, RunningMark.FileName)));

        Assert.Contains("in use", await Server(1, "restore", backup, "--replace", $"--Tangent:DataFolder={tangentSpace.DataFolder}"));
        Assert.Contains("in use", await Server(1, "restore", "--replace", $"--Tangent:DataFolder={tangentSpace.DataFolder}"));
        Assert.True(File.Exists(tangentSpace.DatabasePath));
        Assert.Empty(Directory.GetDirectories(tangentSpace.Folder, "data-*"));

        await tangentSpace.StopAsync(Cancellation);
        // A stopped process holds no database file; the fixture's process stays, so its pooled handles are let go.
        SqliteConnection.ClearAllPools();
        await Server(0, "restore", backup, "--replace", $"--Tangent:DataFolder={tangentSpace.DataFolder}");

        Assert.Single(Directory.GetDirectories(tangentSpace.Folder, "data-before-restore-*"));
        Assert.True(File.Exists(tangentSpace.DatabasePath));
        Assert.False(File.Exists(Path.Combine(tangentSpace.DataFolder, RunningMark.FileName)));
    }

    [Fact]
    public async Task Restore_takes_the_backup_and_replace_in_either_order_and_refuses_a_second_folder()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var backups = Path.Combine(tangentSpace.Folder, "backups");
        await Server(0, "backup", backups, $"--Tangent:DataFolder={tangentSpace.DataFolder}");
        var backup = Assert.Single(Directory.GetDirectories(backups));
        var first = Path.Combine(tangentSpace.Folder, "first");
        var second = Path.Combine(tangentSpace.Folder, "second");
        foreach (var folder in new[] { first, second })
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "kept.txt"), "kept", Cancellation);
        }

        await Server(0, "restore", "--replace", backup, $"--Tangent:DataFolder={first}");
        await Server(0, "restore", "--Tangent:DataFolder", second, backup, "--replace");
        await Server(2, "restore", backup, backup, "--replace", $"--Tangent:DataFolder={first}");

        foreach (var folder in new[] { first, second })
        {
            Assert.True(File.Exists(Path.Combine(folder, TangentSpaceOptions.DatabaseFile)));
            Assert.Single(Directory.GetDirectories(tangentSpace.Folder, Path.GetFileName(folder) + "-before-restore-*"));
        }
    }

    /// <summary>Runs the server executable with <paramref name="arguments"/>, asserts its exit code and returns
    /// what it wrote.</summary>
    private async Task<string> Server(int expectedExitCode, params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(TangentModule).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var written = Task.WhenAll(process.StandardOutput.ReadToEndAsync(Cancellation), process.StandardError.ReadToEndAsync(Cancellation));
        await process.WaitForExitAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(60), Cancellation);
        var text = string.Concat(await written);
        output.WriteLine($"{string.Join(' ', arguments)} -> {process.ExitCode}: {text}");
        Assert.True(process.ExitCode == expectedExitCode,
            $"{string.Join(' ', arguments)} exited {process.ExitCode}, not {expectedExitCode}: {text}");
        return text;
    }
}
