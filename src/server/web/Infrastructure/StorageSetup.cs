using System.Security.Claims;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Data.Core.Routing;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Connector.DuckDb;
using Koan.Data.Connector.Json;
using Koan.Data.Connector.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Tangent.Community;
using Tangent.Identity;
using Tangent.Spaces;

namespace Tangent.Infrastructure;

/// <summary>First-install database selection. Candidate probes never change Koan's active source.</summary>
public sealed class StorageSetup(
    DataProviderSetup providers,
    DataProviderCatalog catalog,
    DefaultDataRouteAuthority activeRoute,
    DataSourceRegistry sources,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    IMemoryCache tickets,
    IOptions<SpaceOptions> spaceOptions,
    IOptions<SqliteOptions> sqliteOptions,
    IOptions<DuckDbOptions> duckDbOptions,
    IOptions<JsonDataOptions> jsonOptions,
    ParticipantDirectory directory,
    ServerGovernance governance)
{
    private readonly SemaphoreSlim _changeGate = new(1, 1);
    private readonly string _configurationPath = Path.GetFullPath(configuration["Tangent:Storage:ConfigurationPath"]
        ?? Path.Combine(environment.ContentRootPath, "data", "storage.json"));
    private readonly string? _persistentRoot = configuration["Tangent:Storage:PersistentRoot"] is { Length: > 0 } root
        ? Path.GetFullPath(root) : null;
    private bool _pendingRestart;

    public bool PendingRestart => Volatile.Read(ref _pendingRestart);
    private DataProviderCandidate? ActiveCandidate => providers.Candidates.FirstOrDefault(candidate =>
        candidate.Id.Equals(activeRoute.Current.Adapter, StringComparison.OrdinalIgnoreCase)
        || candidate.Aliases.Contains(activeRoute.Current.Adapter, StringComparer.OrdinalIgnoreCase));

    public string? ActiveStorageIssue => ActiveCandidate is not { } candidate
        ? "The active database adapter is not available. Configure a persistent adapter before creating the first Tangent."
        : !HasPersistency(candidate)
            ? "The active database adapter does not declare persistent storage support. Choose a persistent database before creating the first Tangent."
            : ActiveFileIssue(candidate);

    public async Task<IDisposable> EnterChange(CancellationToken ct)
    {
        await _changeGate.WaitAsync(ct);
        return new Lease(_changeGate);
    }

    public async Task<object> Describe(ClaimsPrincipal principal, CancellationToken ct)
    {
        var firstRun = await RequireOperator(principal, ct);
        return new
        {
            activeProvider = ActiveCandidate?.Id ?? activeRoute.Current.Adapter,
            activeDurable = ActiveStorageIssue is null,
            activeStorageIssue = ActiveStorageIssue,
            providers = providers.Candidates.Where(HasPersistency).Select(candidate =>
            {
                var reason = UnavailableReason(candidate);
                return new
                {
                    id = candidate.Id,
                    name = candidate.DisplayName,
                    availableForSetup = reason is null,
                    unavailableReason = reason,
                    fields = candidate.Fields.Select(field => new
                    {
                        name = field.Key,
                        label = field.Label,
                        required = (candidate.Id.Equals("json", StringComparison.OrdinalIgnoreCase)
                            && field.Key.Equals("DirectoryPath", StringComparison.OrdinalIgnoreCase))
                            || field.Required && field.DefaultValue is null,
                        secret = field.Kind == Koan.Data.Abstractions.DataProviderSetupFieldKind.Secret,
                        type = field.Kind == Koan.Data.Abstractions.DataProviderSetupFieldKind.Integer ? "number" : "text",
                        placeholder = SetupPlaceholder(candidate.Id, field.Key, field.Placeholder)
                    })
                };
            }),
            changeRequiresRestart = true,
            firstRun,
            pendingRestart = PendingRestart
        };
    }

    public async Task<StorageTestResult> Test(
        ClaimsPrincipal principal, string provider, IReadOnlyDictionary<string, string?> settings, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(settings);
        await RequireFirstRun(principal, ct);
        if (PendingRestart) throw new InvalidOperationException("Restart the server before changing its database again.");
        if (settings.Count > 24 || settings.Any(item => item.Key.Length > 80 || item.Value?.Length > 16_384))
            throw new ArgumentException("Database settings are too large.");
        var candidate = providers.Candidates.FirstOrDefault(item =>
            string.Equals(item.Id, provider, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
            throw new ArgumentException("This database adapter is not available for tested setup.");
        if (UnavailableReason(candidate) is { } reason) throw new ArgumentException(reason);
        if (candidate.Id.Equals("json", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(settings.FirstOrDefault(item =>
                item.Key.Equals("DirectoryPath", StringComparison.OrdinalIgnoreCase)).Value))
            throw new ArgumentException("Choose a JSON directory inside the persistent state root.");
        var connection = settings.FirstOrDefault(item =>
            item.Key.Equals("ConnectionString", StringComparison.OrdinalIgnoreCase)).Value;
        if (candidate.Id.Equals("sqlite", StringComparison.OrdinalIgnoreCase) && connection is not null)
        {
            if (SqliteTargetIssue(connection) is { } issue) throw new ArgumentException(issue);
        }
        if (candidate.Id.Equals("duckdb", StringComparison.OrdinalIgnoreCase)
            && DuckDbTargetIssue(connection) is { } duckIssue)
            throw new ArgumentException(duckIssue);
        RequirePersistentFileTarget(candidate, settings, connection);

        var result = await providers.Probe(candidate.Id, settings, ct);
        if (!result.IsSuccessful)
            throw new InvalidOperationException(result.Message + (result.Correction is null ? "" : " " + result.Correction));

        var actor = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var testId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        tickets.Set("storage:" + testId,
            new TestedCandidate(actor, candidate.Id,
                settings.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)),
            TimeSpan.FromMinutes(10));
        return new StorageTestResult(testId, candidate.Id, result.Message);
    }

    public async Task<StorageActivationResult> Activate(ClaimsPrincipal principal, string testId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testId);
        using var lease = await EnterChange(ct);
        await RequireFirstRun(principal, ct);
        if (PendingRestart) throw new InvalidOperationException("Restart the server to apply the saved database configuration.");
        if (testId.Length is < 32 or > 64 || !tickets.TryGetValue("storage:" + testId, out TestedCandidate? tested)
            || tested is null || tested.Actor != ParticipationAccess.Require(principal, ParticipationGrants.Read))
            throw new ArgumentException("The connection test expired. Test this database again.");
        tickets.Remove("storage:" + testId);

        // This file is deliberately outside the selected database: startup can discover the choice
        // before Koan creates repositories. A same-directory rename makes the complete choice visible.
        var parent = Path.GetDirectoryName(_configurationPath)!;
        Directory.CreateDirectory(parent);
        var temporary = _configurationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var settings = new Dictionary<string, string?>(tested.Settings, StringComparer.OrdinalIgnoreCase)
            {
                ["Adapter"] = tested.Provider
            };
            var document = JsonSerializer.Serialize(new
            {
                Koan = new { Data = new { Sources = new { Default = settings } } }
            });
            await File.WriteAllTextAsync(temporary, document, ct);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, _configurationPath, true);
            Volatile.Write(ref _pendingRestart, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("The database choice could not be saved to persistent storage. Check the mounted state directory.", error);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { /* The failed temporary file is not an active configuration. */ }
        }
        return new StorageActivationResult(tested.Provider, true,
            "Database choice saved. Restart Tangent, then sign in again to create your first Tangent.");
    }

    private async Task RequireFirstRun(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!await RequireOperator(principal, ct))
            throw new InvalidOperationException("This Space already contains data. Moving it requires a data transfer.");
    }

    private async Task<bool> RequireOperator(ClaimsPrincipal principal, CancellationToken ct)
    {
        var actor = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        using var fresh = EntityContext.NoCache();
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        if (space is not null)
        {
            if (!space.IsOwner(actor)) throw new UnauthorizedAccessException("Only the Space owner can inspect database settings.");
            return false;
        }
        var did = principal.FindFirst(Koan.Web.Auth.Connector.Atproto.AtprotoClaimTypes.Did)?.Value;
        if (did is null || !string.IsNullOrWhiteSpace(spaceOptions.Value.OwnerDid)
            && !string.Equals(spaceOptions.Value.OwnerDid, did, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Sign in with the account that can establish this Space.");
        if (await directory.ByDid(did, ct) is not { } holder || holder.Id != actor
            || !(await governance.Read(actor, ct)).CanClaim)
            throw new UnauthorizedAccessException("This account cannot establish the Space.");
        return true;
    }

    private static bool HasPersistency(DataProviderCandidate candidate)
        => candidate.Capabilities.Contains(DataCaps.Persistency.Id, StringComparer.Ordinal);

    private string? ActiveFileIssue(DataProviderCandidate candidate)
    {
        if (candidate.Id is not ("sqlite" or "duckdb" or "json")) return null;
        try
        {
            var source = activeRoute.Current.Source;
            var owner = catalog.Find(candidate.Id)
                ?? throw new InvalidOperationException("The active database adapter is no longer installed.");
            var connection = candidate.Id switch
            {
                "sqlite" => AdapterConnectionResolver.ResolveRoutedConnection(configuration, sources,
                    candidate.Id, source, sqliteOptions.Value.ConnectionString, owner),
                "duckdb" => AdapterConnectionResolver.ResolveRoutedConnection(configuration, sources,
                    candidate.Id, source, duckDbOptions.Value.ConnectionString, owner),
                _ => null
            };
            var directory = candidate.Id == "json"
                ? AdapterConnectionResolver.GetSourceSetting(configuration, sources, candidate.Id, source,
                    nameof(JsonDataOptions.DirectoryPath), jsonOptions.Value.DirectoryPath, owner)
                : null;
            if (candidate.Id == "sqlite" && SqliteTargetIssue(connection) is { } sqliteIssue)
                return sqliteIssue;
            if (candidate.Id == "duckdb" && DuckDbTargetIssue(connection) is { } duckIssue)
                return duckIssue;
            if (_persistentRoot is null) return null;
            if (candidate.Id == "json" && string.IsNullOrWhiteSpace(directory))
                return "Configure the active JSON directory inside the persistent state root.";
            if (candidate.Id != "json" && (string.IsNullOrWhiteSpace(connection) || connection == "auto"))
                return "Configure the active database file inside the persistent state root.";
            var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (directory is not null) settings["DirectoryPath"] = directory;
            RequirePersistentFileTarget(candidate, settings, connection);
            return null;
        }
        catch (ArgumentException error)
        {
            return error.Message;
        }
        catch (InvalidOperationException)
        {
            return "The active database target could not be resolved. Configure persistent storage before creating the first Tangent.";
        }
    }

    private static string? SqliteTargetIssue(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection) || connection.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return "Choose a SQLite file path so content survives a restart.";
        var parsed = new SqliteConnectionStringBuilder(connection);
        return parsed.Mode == SqliteOpenMode.Memory
            || string.IsNullOrWhiteSpace(parsed.DataSource)
            || parsed.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || parsed.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? "Choose a SQLite file path so content survives a restart; SQLite memory and URI targets are unavailable."
            : null;
    }

    private static string? DuckDbTargetIssue(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection) || connection.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return "Choose a DuckDB file path so content survives a restart.";
        string? dataSource = null;
        var memoryMode = false;
        foreach (var segment in DuckDbSegments(connection))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0) continue;
            var key = segment[..separator].Trim();
            var value = segment[(separator + 1)..].Trim();
            if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)
                || key.Equals("DataSource", StringComparison.OrdinalIgnoreCase)) dataSource = value;
            if (key.Equals("Mode", StringComparison.OrdinalIgnoreCase)
                && value.Equals("Memory", StringComparison.OrdinalIgnoreCase)) memoryMode = true;
        }
        return memoryMode || string.IsNullOrWhiteSpace(dataSource)
            || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            ? "Choose a DuckDB file path so content survives a restart; memory mode is unavailable."
            : null;
    }

    // Match Koan DuckDbConnections.SplitSegments: semicolons inside quoted values are not separators.
    private static IEnumerable<string> DuckDbSegments(string connection)
    {
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in connection)
        {
            if (character == '"') quoted = !quoted;
            if (character == ';' && !quoted)
            {
                if (current.Length > 0) yield return current.ToString().Trim();
                current.Clear();
            }
            else current.Append(character);
        }
        if (current.Length > 0) yield return current.ToString().Trim();
    }

    private string? UnavailableReason(DataProviderCandidate candidate)
    {
        if (!HasPersistency(candidate)) return "This adapter does not declare persistent storage support.";
        if (!candidate.SupportsProbe) return "This adapter does not support a connection test, so it cannot be activated during setup.";
        if (candidate.Id.Equals("json", StringComparison.OrdinalIgnoreCase) && _persistentRoot is null)
            return "JSON storage needs a configured persistent state root.";
        return null;
    }

    private string? SetupPlaceholder(string provider, string field, string? fallback)
    {
        if (_persistentRoot is null) return fallback;
        if (field.Equals("ConnectionString", StringComparison.OrdinalIgnoreCase))
        {
            if (provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
                return "Data Source=" + Path.Combine(_persistentRoot, "tangent.sqlite");
            if (provider.Equals("duckdb", StringComparison.OrdinalIgnoreCase))
                return "Data Source=" + Path.Combine(_persistentRoot, "tangent.duckdb");
        }
        return provider.Equals("json", StringComparison.OrdinalIgnoreCase)
            && field.Equals("DirectoryPath", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(_persistentRoot, "tangent-json") : fallback;
    }

    private void RequirePersistentFileTarget(DataProviderCandidate candidate,
        IReadOnlyDictionary<string, string?> settings, string? connection)
    {
        if (_persistentRoot is null) return;
        string? path = null;
        if (candidate.Id.Equals("sqlite", StringComparison.OrdinalIgnoreCase) && connection is not null)
        {
            path = new SqliteConnectionStringBuilder(connection).DataSource;
            if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use a file path in the mounted state directory, not a SQLite URI.");
        }
        else if (candidate.Id.Equals("duckdb", StringComparison.OrdinalIgnoreCase) && connection is not null)
        {
            path = DuckDbSegments(connection).Select(segment => segment.Split('=', 2))
                .Where(parts => parts.Length == 2 && (parts[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase)
                    || parts[0].Trim().Equals("DataSource", StringComparison.OrdinalIgnoreCase)))
                .Select(parts => parts[1].Trim()).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Choose a DuckDB file path in the mounted state directory.");
        }
        else if (candidate.Id.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            path = settings.FirstOrDefault(item => item.Key.Equals("DirectoryPath", StringComparison.OrdinalIgnoreCase)).Value
                ?? throw new ArgumentException("Choose a JSON directory inside the persistent state root.");
        }
        if (path is null) return;
        if (path.Length == 0 || path.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException("Choose a file or directory in the mounted state directory.");
        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path));
        var relative = Path.GetRelativePath(_persistentRoot, fullPath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new ArgumentException($"Choose a path inside the mounted state directory ({_persistentRoot}).");
    }

    private sealed record TestedCandidate(string Actor, string Provider, IReadOnlyDictionary<string, string?> Settings);
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

public sealed record StorageTestResult(string TestId, string Provider, string Message);
public sealed record StorageActivationResult(string Provider, bool RestartRequired, string Message);
