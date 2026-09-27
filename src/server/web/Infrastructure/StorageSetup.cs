using System.Security.Claims;
using System.Text.Json;
using Koan.Data.Core;
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
    IConfiguration configuration,
    IWebHostEnvironment environment,
    IMemoryCache tickets,
    IOptions<SpaceOptions> spaceOptions,
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
            activeProvider = configuration["Koan:Data:Sources:Default:Adapter"] ?? "sqlite",
            providers = providers.Candidates.Select(candidate => new
            {
                id = candidate.Id,
                name = IsVolatile(candidate.Id)
                    ? candidate.DisplayName + " (temporary; data is lost on restart)"
                    : candidate.DisplayName,
                fields = candidate.Fields.Select(field => new
                {
                    name = field.Key,
                    label = field.Label,
                    required = field.Required && field.DefaultValue is null,
                    secret = field.Kind == Koan.Data.Abstractions.DataProviderSetupFieldKind.Secret,
                    type = field.Kind == Koan.Data.Abstractions.DataProviderSetupFieldKind.Integer ? "number" : "text",
                    placeholder = field.Placeholder
                })
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
        if (IsVolatile(provider))
            throw new ArgumentException("In-memory storage loses all content on restart. Choose a durable database.");
        var candidate = providers.Candidates.FirstOrDefault(item =>
            string.Equals(item.Id, provider, StringComparison.OrdinalIgnoreCase));
        if (candidate is null || !candidate.SupportsProbe)
            throw new ArgumentException("This database adapter is not available for tested setup.");
        var connection = settings.FirstOrDefault(item =>
            item.Key.Equals("ConnectionString", StringComparison.OrdinalIgnoreCase)).Value;
        if (candidate.Id.Equals("sqlite", StringComparison.OrdinalIgnoreCase) && connection is not null)
        {
            var parsed = new SqliteConnectionStringBuilder(connection);
            if (parsed.Mode == SqliteOpenMode.Memory || parsed.DataSource == ":memory:")
                throw new ArgumentException("Choose a SQLite file path so content survives a restart.");
        }
        if (candidate.Id.Equals("duckdb", StringComparison.OrdinalIgnoreCase)
            && connection?.Contains(":memory:", StringComparison.OrdinalIgnoreCase) == true)
            throw new ArgumentException("Choose a DuckDB file path so content survives a restart.");
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

    private static bool IsVolatile(string provider)
        => provider.Equals("inmemory", StringComparison.OrdinalIgnoreCase)
            || provider.Equals("memory", StringComparison.OrdinalIgnoreCase);

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
            path = connection.Split(';').Select(segment => segment.Split('=', 2))
                .Where(parts => parts.Length == 2 && (parts[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase)
                    || parts[0].Trim().Equals("DataSource", StringComparison.OrdinalIgnoreCase)))
                .Select(parts => parts[1].Trim()).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Choose a DuckDB file path in the mounted state directory.");
        }
        else if (candidate.Id.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            path = settings.FirstOrDefault(item => item.Key.Equals("DirectoryPath", StringComparison.OrdinalIgnoreCase)).Value
                ?? candidate.Fields.FirstOrDefault(field => field.Key.Equals("DirectoryPath", StringComparison.OrdinalIgnoreCase))?.DefaultValue;
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
