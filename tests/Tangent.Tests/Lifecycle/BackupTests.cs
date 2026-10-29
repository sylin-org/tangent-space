using System.Diagnostics;
using Koan.Data.Core;
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
            new Dictionary<string, string?> { ["Tangent:BaseUrl"] = "https://backed-up.example" },
            ct: Cancellation);
        var row = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        row!.Name = "The workshop's place";
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
                ["Tangent:BaseUrl"] = "https://restored.example",
                ["Tangent:DataFolder"] = restoredFolder
            },
            ct: Cancellation);
        var read = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);

        Assert.Equal(restoredFolder, restored.DataFolder);
        Assert.Equal("The workshop's place", read?.Name);
        Assert.Equal("https://backed-up.example", read?.BaseUrl);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(restoredFolder, "media", "cover.bin"), Cancellation));
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
        Assert.Equal(expectedExitCode, process.ExitCode);
        return text;
    }
}
