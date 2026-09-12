using System.Net;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using TangentSpace.Participants;
using Xunit;

namespace TangentSpace.Tests.ExperienceIntegration;

/// <summary>The /u/{identifier} resolver over the real web application: perennial DID forms,
/// handle canonicalization redirects, and the honest-miss policy (stale, lookalike, empty,
/// oversized, ambiguous, and not-yet-existing tangent:local: forms), plus the profile API
/// accepting the same identifier forms. Policy never participates: routing decisions here are
/// identity-level only.</summary>
[Xunit.Collection("Experience integration")]
public sealed class UserPageResolutionTests : IAsyncLifetime
{
    private ExperienceWebApp app = null!;
    private HttpClient pages = null!;

    public async ValueTask InitializeAsync()
    {
        app = await ExperienceWebApp.StartAsync();
        // The fixture boots from an empty temp web root; shell assertions must exercise the
        // shipped markup, not a fixture copy.
        var environment = app.Services.GetRequiredService<IWebHostEnvironment>();
        Directory.CreateDirectory(environment.WebRootPath!);
        File.Copy(Shipped("wwwroot/index.html"), Path.Combine(environment.WebRootPath!, "index.html"), true);
        pages = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Origin) };
    }

    public async ValueTask DisposeAsync()
    {
        pages.Dispose();
        await app.DisposeAsync();
    }

    [Fact]
    public async Task A_did_that_holds_a_handle_redirects_to_the_handle_page()
    {
        using var response = await pages.GetAsync("/u/" + ExperienceWebApp.AgentDid);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/u/" + ExperienceWebApp.AgentHandle, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_bare_handle_serves_the_shell_with_the_profile_section()
    {
        using var response = await pages.GetAsync("/u/" + ExperienceWebApp.AgentHandle);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var shell = await response.Content.ReadAsStringAsync();
        // The profile section must exist for the JS renderer, ahead of the noscript block.
        Assert.Contains("id=\"participant-profile\"", shell);
        Assert.True(shell.IndexOf("id=\"participant-profile\"", StringComparison.Ordinal)
            < shell.IndexOf("<noscript", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_did_without_a_handle_serves_the_shell()
    {
        await Arrive("did:plc:experienceghostAAAAAA", null);
        using var response = await pages.GetAsync("/u/did:plc:experienceghostAAAAAA");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_leading_at_handle_redirects_to_the_bare_form()
    {
        using var response = await pages.GetAsync("/u/@" + ExperienceWebApp.AgentHandle);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/u/" + ExperienceWebApp.AgentHandle, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_case_variant_handle_resolves_and_redirects_to_the_stored_form()
    {
        using var response = await pages.GetAsync("/u/" + ExperienceWebApp.AgentHandle.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/u/" + ExperienceWebApp.AgentHandle, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_stale_or_lookalike_handle_misses_honestly()
    {
        using var stale = await pages.GetAsync("/u/former.experience.test");
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        using var lookalike = await pages.GetAsync("/u/" + ExperienceWebApp.AgentHandle + ".evil");
        Assert.Equal(HttpStatusCode.NotFound, lookalike.StatusCode);
    }

    [Fact]
    public async Task An_empty_or_oversized_identifier_misses_honestly()
    {
        using var whitespace = await pages.GetAsync("/u/%20");
        Assert.Equal(HttpStatusCode.NotFound, whitespace.StatusCode);
        using var oversized = await pages.GetAsync("/u/" + new string('a', 300));
        Assert.Equal(HttpStatusCode.NotFound, oversized.StatusCode);
    }

    [Fact]
    public async Task An_ambiguous_handle_never_picks_a_winner()
    {
        // Stored case variants of one handle must collide under the case-folded check.
        await Arrive("did:plc:experiencetwinsAAAAAA", "twin.experience.test");
        await Arrive("did:plc:experiencetwinnAAAAAA", "TWIN.experience.test");
        using var response = await pages.GetAsync("/u/twin.experience.test");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_local_identity_form_misses_honestly_until_participants_carry_one()
    {
        using var response = await pages.GetAsync("/u/tangent:local:0123456789abcdef");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_profile_api_accepts_a_handle_and_returns_the_canonical_did_data()
    {
        const string avatarFile = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png";
        app.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Set(
            "tangent-profile:" + ExperienceWebApp.AgentDid,
            new ParticipantProfileSnapshot
            {
                Id = ExperienceWebApp.AgentDid,
                DisplayName = "A familiar companion",
                Description = "Here for the conversation.",
                AvatarFile = avatarFile,
                CapturedAt = DateTimeOffset.UtcNow,
                RetryAfter = DateTimeOffset.UtcNow.AddHours(1)
            });
        using var response = await app.Http.GetAsync($"/api/participants/{ExperienceWebApp.AgentHandle}/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        var data = root.GetProperty("result").GetProperty("data");
        Assert.Equal(ExperienceWebApp.AgentDid, data.GetProperty("did").GetString());
        Assert.Equal(ExperienceWebApp.AgentHandle, data.GetProperty("handle").GetString());
        Assert.Equal("A familiar companion", data.GetProperty("displayName").GetString());
        Assert.Equal("/api/profile-cache/avatar?did=" + Uri.EscapeDataString(ExperienceWebApp.AgentDid)
            + "&v=" + avatarFile, data.GetProperty("avatar").GetString());
        Assert.StartsWith("/u/", data.GetProperty("profileUrl").GetString());
        var posts = data.GetProperty("posts").EnumerateArray().ToArray();
        Assert.NotEmpty(posts);
        Assert.All(posts, post => Assert.StartsWith(app.Origin + "/t/" + ExperienceWebApp.TangentKey + "/",
            post.GetProperty("url").GetString()));
        using var decorated = await app.Http.GetAsync($"/api/participants/@{ExperienceWebApp.AgentHandle}/profile");
        Assert.Equal(HttpStatusCode.OK, decorated.StatusCode);
        using var decoratedDocument = JsonDocument.Parse(await decorated.Content.ReadAsStringAsync());
        Assert.Equal("ok", decoratedDocument.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_profile_api_miss_is_the_blocked_outcome_the_client_renders()
    {
        using var response = await app.Http.GetAsync("/api/participants/nobody.experience.test/profile");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.Equal("No participant is registered under that identity.",
            root.GetProperty("result").GetProperty("problem").GetProperty("message").GetString());
    }

    /// <summary>First arrival through the same enrollment path the auth flow uses.</summary>
    private static async Task Arrive(string did, string? handle)
    {
        using var context = EntityContext.NoCache();
        var (participant, identities) = Participant.Enroll(did, handle, DateTimeOffset.UtcNow);
        await participant.Save();
        foreach (var identity in identities) await identity.Save();
    }

    private static string Shipped(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "server", "web", relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("The repository's shipped web root could not be located.");
    }
}
