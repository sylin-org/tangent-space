using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Identity;
using Tangent.Infrastructure;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

/// <summary>Database setup must never reinterpret an established Space as an empty install.</summary>
[Xunit.Collection("Experience integration")]
public sealed class StorageSetupTests
{
    [Theory]
    [InlineData("inmemory", null, "persistent storage support")]
    [InlineData("sqlite", "Data Source=:memory:", "memory")]
    [InlineData("sqlite", "Data Source=:MEMORY:", "memory")]
    [InlineData("duckdb", "Data Source=/tmp/tangent.duckdb;Mode=Memory", "memory mode")]
    [InlineData("json", null, "mounted state directory")]
    public async Task Active_lossy_or_unmounted_storage_blocks_first_tangent(
        string adapter, string? connection, string expectedIssue)
    {
        TestHooks.ResetDataConfigs();
        var root = Path.Combine(Path.GetTempPath(), "Tangent-active-storage", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(root);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = root, EnvironmentName = "Testing"
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:Default:Adapter"] = adapter,
            ["Koan:Data:Sources:Default:ConnectionString"] = connection,
            ["Koan:Data:Route:StatePath"] = Path.Combine(root, "route.json"),
            ["Tangent:Storage:PersistentRoot"] = Path.Combine(root, "mounted")
        });
        builder.Services.AddKoan();
        await using var app = builder.Build();
        try
        {
            var issue = app.Services.GetRequiredService<StorageSetup>().ActiveStorageIssue;
            Assert.Contains(expectedIssue, issue, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Theory]
    [InlineData("sqlite", "Koan:Data:Sources:Default:sqlite:ConnectionString", "Data Source=:memory:", "memory")]
    [InlineData("sqlite", "ConnectionStrings:Default", "Data Source=:MEMORY:", "memory")]
    [InlineData("duckdb", "Koan:Data:Sources:Default:duckdb:ConnectionString", "Data Source={root}/active.duckdb;Mode=Memory", "memory mode")]
    [InlineData("json", "Koan:Data:Sources:Default:json:DirectoryPath", "{outside}/json", "mounted state directory")]
    public async Task Active_provider_scoped_target_uses_koan_resolution_and_rejects_loss(
        string adapter, string key, string value, string expectedIssue)
    {
        var issue = await ActiveIssue(adapter, key, value);
        Assert.Contains(expectedIssue, issue, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sqlite", "Koan:Data:Sources:Default:sqlite:ConnectionString", "Data Source={root}/active.sqlite")]
    [InlineData("duckdb", "Koan:Data:Sources:Default:duckdb:ConnectionString", "Data Source={root}/active.duckdb")]
    [InlineData("json", "Koan:Data:Sources:Default:json:DirectoryPath", "{root}/json")]
    public async Task Active_provider_scoped_file_inside_persistent_root_is_accepted(
        string adapter, string key, string value)
        => Assert.Null(await ActiveIssue(adapter, key, value));

    [Fact]
    public async Task Sqlite_memory_target_is_rejected_without_a_configured_persistent_root()
        => Assert.Contains("memory", await ActiveIssue("sqlite", "ConnectionStrings:Default",
            "Data Source=:MEMORY:", persistentRoot: false), StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> ActiveIssue(string adapter, string key, string value,
        bool persistentRoot = true)
    {
        TestHooks.ResetDataConfigs();
        var root = Path.Combine(Path.GetTempPath(), "Tangent-active-scoped", Guid.CreateVersion7().ToString("n"));
        var mounted = Path.Combine(root, "mounted");
        Directory.CreateDirectory(root);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = root, EnvironmentName = "Testing"
        });
        var settings = new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:Default:Adapter"] = adapter,
            ["Koan:Data:Route:StatePath"] = Path.Combine(root, "route.json"),
            [key] = value.Replace("{root}", mounted, StringComparison.Ordinal)
                .Replace("{outside}", root, StringComparison.Ordinal)
        };
        if (persistentRoot) settings["Tangent:Storage:PersistentRoot"] = mounted;
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddKoan();
        await using var app = builder.Build();
        try { return app.Services.GetRequiredService<StorageSetup>().ActiveStorageIssue; }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Tested_first_install_choice_is_saved_without_switching_the_live_source()
    {
        TestHooks.ResetDataConfigs();
        var root = Path.Combine(Path.GetTempPath(), "Tangent-storage-choice", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(root);
        var active = Path.Combine(root, "active.sqlite");
        var candidate = Path.Combine(root, "candidate.sqlite");
        var configurationPath = Path.Combine(root, "storage.json");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = root, EnvironmentName = "Testing"
        });
        const string did = "did:plc:storageowneraaaaaaaa";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tangent:Space:OwnerDid"] = did,
            ["Tangent:Storage:ConfigurationPath"] = configurationPath,
            ["Tangent:Storage:PersistentRoot"] = root,
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={active}",
            ["Koan:Data:Sqlite:ConnectionString"] = $"Data Source={active}",
            ["Koan:Cache:Adapters:Sqlite:DatabasePath"] = Path.Combine(root, "cache.db")
        });
        builder.Services.AddKoan();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
            AppHost.Current = app.Services;
            var (participant, identities) = Participant.Enroll(did, "owner.test", DateTimeOffset.UtcNow);
            using (EntityContext.NoCache())
            {
                await participant.Save(TestContext.Current.CancellationToken);
                foreach (var identity in identities) await identity.Save(TestContext.Current.CancellationToken);
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ParticipationConstants.ParticipantClaim, participant.Id),
                 new Claim(AtprotoClaimTypes.Did, did)], "integration"));
            var setup = app.Services.GetRequiredService<StorageSetup>();

            using (var described = JsonSerializer.SerializeToDocument(
                await setup.Describe(principal, TestContext.Current.CancellationToken)))
            {
                var listed = described.RootElement.GetProperty("providers").EnumerateArray().ToArray();
        Assert.DoesNotContain(listed, item => item.GetProperty("id").GetString() == "inmemory");
                Assert.DoesNotContain(listed, item => item.GetProperty("id").GetString() == "redis");
                Assert.True(described.RootElement.GetProperty("activeDurable").GetBoolean());
                Assert.Equal(JsonValueKind.Null, described.RootElement.GetProperty("activeStorageIssue").ValueKind);
                Assert.Equal("Data Source=" + Path.Combine(root, "tangent.sqlite"),
                    listed.Single(item => item.GetProperty("id").GetString() == "sqlite")
                        .GetProperty("fields")[0].GetProperty("placeholder").GetString());
                Assert.Equal("Data Source=" + Path.Combine(root, "tangent.duckdb"),
                    listed.Single(item => item.GetProperty("id").GetString() == "duckdb")
                        .GetProperty("fields")[0].GetProperty("placeholder").GetString());
                var json = listed.Single(item => item.GetProperty("id").GetString() == "json");
                Assert.True(json.GetProperty("availableForSetup").GetBoolean());
                Assert.True(json.GetProperty("fields")[0].GetProperty("required").GetBoolean());
                Assert.Equal(Path.Combine(root, "tangent-json"),
                    json.GetProperty("fields")[0].GetProperty("placeholder").GetString());
            }
            await Assert.ThrowsAsync<ArgumentException>(() => setup.Test(principal, "json",
                new Dictionary<string, string?>(), TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(() => setup.Test(principal, "duckdb",
                new Dictionary<string, string?>
                {
                    ["ConnectionString"] = $"Data Source={Path.Combine(root, "lossy.duckdb")};Mode=Memory"
                }, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(() => setup.Test(principal, "duckdb",
                new Dictionary<string, string?> { ["ConnectionString"] = "Mode=ReadWrite" },
                TestContext.Current.CancellationToken));

            var tested = await setup.Test(principal, "sqlite",
                new Dictionary<string, string?> { ["ConnectionString"] = $"Data Source={candidate}" },
                TestContext.Current.CancellationToken);
            Assert.False(File.Exists(candidate)); // The probe must not create the database.
            var activated = await setup.Activate(principal, tested.TestId, TestContext.Current.CancellationToken);
            Assert.True(activated.RestartRequired);
            Assert.True(setup.PendingRestart);
            Assert.Equal("sqlite", builder.Configuration["Koan:Data:Sources:Default:Adapter"]);
            Assert.Equal($"Data Source={active}", builder.Configuration["Koan:Data:Sources:Default:ConnectionString"]);
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(configurationPath,
                TestContext.Current.CancellationToken));
            Assert.Equal($"Data Source={candidate}", saved.RootElement.GetProperty("Koan")
                .GetProperty("Data").GetProperty("Sources").GetProperty("Default")
                .GetProperty("ConnectionString").GetString());
            await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Test(principal, "sqlite",
                new Dictionary<string, string?> { ["ConnectionString"] = $"Data Source={candidate}" },
                TestContext.Current.CancellationToken));
        }
        finally
        {
            await app.StopAsync();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Established_space_exposes_the_selected_provider_but_refuses_a_new_database()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        app.Http.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);

        using var state = await app.Http.GetAsync("/api/server/storage", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
        using var document = JsonDocument.Parse(await state.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("sqlite", document.RootElement.GetProperty("activeProvider").GetString());
        Assert.False(document.RootElement.GetProperty("firstRun").GetBoolean());
        Assert.False(document.RootElement.GetProperty("pendingRestart").GetBoolean());
        Assert.Contains(document.RootElement.GetProperty("providers").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "postgres");

        using var test = await app.Http.PostAsJsonAsync("/api/server/storage/test",
            new { provider = "sqlite", settings = new { ConnectionString = "Data Source=elsewhere.sqlite" },
                expectedDid = ExperienceWebApp.OwnerDid },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, test.StatusCode);
    }
}
