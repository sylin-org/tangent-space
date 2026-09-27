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
            new { provider = "sqlite", settings = new { ConnectionString = "Data Source=elsewhere.sqlite" } },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, test.StatusCode);
    }
}
