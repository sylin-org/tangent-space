using Tangent.Space;

namespace Tangent.Lifecycle;

/// <summary>The executable's own lifecycle verbs, run instead of the Tangent Space when the first argument names one:
/// <c>backup &lt;folder&gt;</c>, <c>restore [&lt;backup-folder&gt;] [--replace]</c> and <c>archive &lt;folder&gt;</c>. Each reads
/// the same configuration the Tangent Space reads (appsettings.json beside the executable, environment variables, and any
/// remaining <c>--Section:Key=value</c> arguments) to find the data folder; only an archive, which reads the place, needs a
/// Koan host, and one that listens nowhere.</summary>
public sealed class LifecycleCommand
{
    private const string Usage =
        "Tangent.Server backup <folder>                      back up the data folder into a new folder under <folder>\n" +
        "Tangent.Server restore <backup-folder> [--replace]  put a backup back into an empty or absent data folder\n" +
        "Tangent.Server restore --replace                    move the data folder aside; the next start begins anew\n" +
        "Tangent.Server archive <folder>                     render every public page and feed into an empty or absent folder\n" +
        "--replace moves a non-empty data folder aside with a timestamp; nothing is deleted.";

    private LifecycleCommand(string verb, string? folder, bool replace, string[] configuration, bool understood)
    {
        Understood = understood;
        Verb = verb;
        Folder = folder;
        Replace = replace;
        ConfigurationArguments = configuration;
    }

    public string Verb { get; }

    /// <summary>False when the arguments name more than one folder, or --replace for anything but a restore.</summary>
    public bool Understood { get; }

    /// <summary>The folder the verb names: where a backup or an archive goes, or the backup a restore puts back.</summary>
    public string? Folder { get; }

    public bool Replace { get; }

    /// <summary>The arguments left for configuration once the verb and its own arguments are taken out.</summary>
    public string[] ConfigurationArguments { get; }

    /// <summary>The lifecycle verb the arguments name, or null when they start the Tangent Space.</summary>
    public static LifecycleCommand? Parse(string[] args)
    {
        if (args.Length == 0) return null;
        var verb = args[0].ToLowerInvariant();
        if (verb is not ("backup" or "restore" or "archive")) return null;

        // The folder and --replace in any order; every other --Key=value or --Key value is configuration, read as
        // the configuration's own command line reads it. More than one folder is not understood.
        string? folder = null;
        var replace = false;
        var understood = true;
        var configuration = new List<string>();
        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--replace", StringComparison.OrdinalIgnoreCase))
            {
                replace = true;
            }
            else if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                configuration.Add(argument);
                if (!argument.Contains('=') && index + 1 < args.Length) configuration.Add(args[++index]);
            }
            else if (folder is null)
            {
                folder = argument;
            }
            else
            {
                understood = false;
            }
        }

        if (verb != "restore" && replace) understood = false;
        return new LifecycleCommand(verb, understood ? folder : null, replace && understood, configuration.ToArray(), understood);
    }

    /// <summary>Runs the verb against the data folder configured under <paramref name="contentRoot"/> and returns the
    /// process exit code: 0 done, 1 refused or failed, 2 not understood.</summary>
    public int Run(string contentRoot)
    {
        if (!Understood)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

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
                    if (restored.StaleMark is not null)
                        Console.WriteLine($"Ignored a running mark left by {restored.StaleMark}, which is no longer running.");
                    if (restored.MovedAside is not null) Console.WriteLine($"Moved {dataFolder} aside to {restored.MovedAside}");
                    Console.WriteLine(Folder is null
                        ? $"The data folder {dataFolder} is empty; the next start begins a new Tangent Space."
                        : $"Restored {restored.Files} files from {Folder} into {dataFolder}");
                    return 0;
                case "archive" when Folder is not null:
                    var named = Archive.Take(builder, dataFolder, Folder).GetAwaiter().GetResult();
                    Console.WriteLine($"Archived the public pages and feeds to {Folder}; they name the place at {named}.");
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
        catch (OperationCanceledException)
        {
            return Fail($"{Verb} stopped; it left nothing.");
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
