using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Koan.Data.Core;
using Tangent.Identity;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

/// <summary>N-062: nothing could answer "who is on this server". Every listing took the ids it
/// already held, and the only source of ids was a role's member array, so a participant holding
/// no role was invisible although their arrival was recorded. These guard the property that made
/// that possible, not just the endpoint's happy path.</summary>
[Xunit.Collection("Experience integration")]
public sealed class PeopleDirectoryTests
{
    [Fact]
    public async Task The_directory_lists_a_participant_who_holds_no_role_at_all()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        const string did = "did:plc:rolelessarrival000000000";
        const string handle = "roleless-arrival.test";
        using (var context = EntityContext.NoCache())
        {
            // Arrived, never granted anything: exactly the participant the old listings could not name.
            var (participant, identities) = Participant.Enroll(did, handle, DateTimeOffset.UtcNow);
            await participant.Save();
            foreach (var identity in identities) await identity.Save();
        }

        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);
        using var response = await app.Http.GetAsync("/api/people", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var people = document.RootElement.GetProperty("people").EnumerateArray().ToArray();

        var arrival = Assert.Single(people, person => person.GetProperty("did").GetString() == did);
        Assert.Empty(arrival.GetProperty("roles").EnumerateArray());
        Assert.Equal(handle, arrival.GetProperty("handle").GetString());
        // The owner is listed too, and carries the grant that the roleless arrival lacks.
        Assert.Contains(people, person => person.GetProperty("roles").EnumerateArray()
            .Any(role => role.GetString() == "owner"));
    }

    [Fact]
    public async Task Only_the_owner_can_read_the_directory()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.AgentToken);
        using var response = await app.Http.GetAsync("/api/people", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_page_outside_the_allowed_range_is_refused_rather_than_clamped()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);
        using var response = await app.Http.GetAsync("/api/people?page=0", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
