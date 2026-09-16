using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Core;
using Tangent.Identity;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class ArtworkUploadTests
{
    [Fact]
    public async Task Artwork_is_permission_checked_persistent_and_shared_by_server_and_tangent_cards()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        using var owner = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using (EntityContext.NoCache())
        {
            foreach (var (client, participant) in new[] { (owner, app.OwnerParticipantId), (app.Http, app.AgentParticipantId) })
            {
                var (credential, token) = ParticipantCredential.Issue(participant, "Artwork check", 1,
                    [ParticipationGrants.Welcome, ParticipationGrants.Manage], DateTimeOffset.UtcNow, managementPermitted: true);
                await credential.Save();
                client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            }
        }
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l1sAAAAASUVORK5CYII=");
        var upload = new { scope = "server", content = png };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/artwork", upload)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Http.PostAsJsonAsync("/api/artwork", upload)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/artwork", new { scope = "server", content = "not an image"u8.ToArray() })).StatusCode);
        var response = await owner.PostAsJsonAsync("/api/artwork", upload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = (await response.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["url"];
        using var image = await anonymous.GetAsync(url);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await image.Content.ReadAsByteArrayAsync());
        var root = app.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        Assert.True(File.Exists(Path.Combine(root, "artwork", url.Split('/').Last())));
        Assert.Equal(HttpStatusCode.OK, (await owner.PatchAsJsonAsync("/api/server", new { coverImageUrl = url })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/artwork", new { scope = "tangent", tangentKey = ExperienceWebApp.TangentKey, content = png })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PatchAsJsonAsync("/api/v1/tangents/" + ExperienceWebApp.TangentKey, new { artwork = url })).StatusCode);
    }
}
