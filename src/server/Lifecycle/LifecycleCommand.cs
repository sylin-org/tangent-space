using Tangent.Space;

namespace Tangent.Lifecycle;

/// <summary>The executable's own lifecycle verbs, run instead of the Tangent Space when the first argument names one:
/// <c>backup &lt;folder&gt;</c> and <c>restore [&lt;backup-folder&gt;] [--replace]</c>. Each reads the same
/// configuration the Tangent Space reads (appsettings.json beside the executable, environment variables, and any
/// remaining <c>--Section:Key=value</c> arguments) to find the data folder, and needs no Koan host.</summary>
public sealed class LifecycleCommand
{
    private const string Usage =
        "Tangent.Server backup <folder>                      back up the data folder into a new folder under <folder>\n" +
        "Tangent.Server restore <backup-folder> [--replace]  put a backup back into an empty or absent data folder\n" +
        "Tangent.Server restore --replace                    move the data folder aside; the next start begins anew\n" +
        "--replace moves a non-empty data folder aside with a timestamp; nothing is deleted.";

    private LifecycleCommand(string verb, string? folder, bool replace, string[] configuration)
    {
        Verb = verb;
        Folder = folder;
        Replace = replace;
        ConfigurationArguments = configuration;
    }

    public string Verb { get; }

    /// <summary>The folder the verb names: where a backup goes, or the backup a restore puts back.</summary>
    public string? Folder { get; }

    public bool Replace { get; }

    /// <summary>The arguments left for configuration once the verb and its own arguments are taken out.</summary>
    public string[] ConfigurationArguments { get; }

    /// <summary>The lifecycle verb the arguments name, or null when they start the Tangent Space.</summary>
    public static LifecycleCommand? Parse(string[] args)
    {
        if (args.Length == 0) return null;
        var verb = args[0].ToLowerInvariant();
        if (verb is not ("backup" or "restore")) return null;

        var rest = args.Skip(1).ToList();
        string? folder = null;
        if (rest.Count > 0 && !rest[0].StartsWith("--", StringComparison.Ordinal))
        {
            folder = rest[0];
            rest.RemoveAt(0);
        }
        var replace = rest.RemoveAll(argument => argument.Equals("--replace", StringComparison.OrdinalIgnoreCase)) > 0;
        return new LifecycleCommand(verb, folder, replace, rest.ToArray());
    }

    /// <summary>Runs the verb against the data folder configured under <paramref name="contentRoot"/> and returns the
    /// process exit code: 0 done, 1 refused or failed, 2 not understood.</summary>
    public int Run(string contentRoot)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = ConfigurationArguments,
            ContentRootPath = contentRoot
        });
        var options = builder.Configuration.GetSection(TangentSpaceOptions.Configuration).Get<TangentSpaceOptions>() ?? new TangentSpaceOptions();
        if (string.IsNullOrWhiteSpace(options.DataFolder))
            return Fail("Tangent:DataFolder must name a folder.");
        var dataFolder = options.DataFolderPath(builder.Environment.ContentRootPath);

        try
        {
            switch (Verb)
            {
                case "backup" when Folder is not null:
                    Console.WriteLine($"Backed up {dataFolder} to {Backup.Take(dataFolder, Folder)}");
                    return 0;
                case "restore" when Folder is not null || Replace:
                    var restored = Backup.Restore(Folder, dataFolder, Replace);
                    if (restored.MovedAside is not null) Console.WriteLine($"Moved {dataFolder} aside to {restored.MovedAside}");
                    Console.WriteLine(Folder is null
                        ? $"The data folder {dataFolder} is empty; the next start begins a new Tangent Space."
                        : $"Restored {restored.Files} files from {Folder} into {dataFolder}");
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (LifecycleRefusal refusal)
        {
            return Fail(refusal.Message);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            return Fail($"{Verb} failed: {failure.Message}");
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
