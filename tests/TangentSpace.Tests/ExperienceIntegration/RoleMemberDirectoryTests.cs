using System.Security.Claims;
using System.Net;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tangent.Access;
using Tangent.Identity;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class RoleMemberDirectoryTests
{
    [Fact]
    public async Task Derived_roles_show_identity_membership_and_reject_assignment()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var scheme = Koan.Web.Auth.Extensions.AuthenticationExtensions.CookieScheme;
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ParticipationConstants.ParticipantClaim, app.OwnerParticipantId)], "browser"));
        using var browser = new HttpClient(new HttpClientHandler()) { BaseAddress = new Uri(app.Origin) };
        browser.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" +
            options.TicketDataFormat.Protect(new AuthenticationTicket(principal, scheme)));

        async Task<JsonDocument> ReadRoles(string participantId)
        {
            using var response = await browser.GetAsync($"/api/roles/ui/participant/{participantId}/roles", ct);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }

        static JsonElement Role(JsonDocument document, string id)
            => Assert.Single(document.RootElement.EnumerateArray(), role => role.GetProperty("id").GetString() == id);

        using (var ownerRoles = await ReadRoles(app.OwnerParticipantId))
        {
            var owner = Role(ownerRoles, TangentBuiltInRoles.Owner.Token);
            Assert.True(owner.GetProperty("isMember").GetBoolean());
            Assert.True(owner.GetProperty("isProtected").GetBoolean());
            Assert.False(owner.GetProperty("canToggle").GetBoolean());
        }
        using (var humanRoles = await ReadRoles(app.HumanParticipantId))
        {
            var owner = Role(humanRoles, TangentBuiltInRoles.Owner.Token);
            Assert.False(owner.GetProperty("isMember").GetBoolean());
            Assert.False(owner.GetProperty("canToggle").GetBoolean());
            var participant = Role(humanRoles, TangentBuiltInRoles.Participant.Token);
            Assert.True(participant.GetProperty("isMember").GetBoolean());
            Assert.True(participant.GetProperty("isProtected").GetBoolean());
            Assert.False(participant.GetProperty("canToggle").GetBoolean());
        }

        using var session = await browser.GetAsync("/api/roles/ui/session", ct);
        session.EnsureSuccessStatusCode();
        using var tokens = JsonDocument.Parse(await session.Content.ReadAsStringAsync(ct));
        var header = tokens.RootElement.GetProperty("headerName").GetString()!;
        var token = tokens.RootElement.GetProperty("requestToken").GetString()!;
        foreach (var roleId in new[] { TangentBuiltInRoles.Owner.Token, TangentBuiltInRoles.Participant.Token, "role:everyone" })
        {
            var path = $"/api/roles/ui/participant/{app.HumanParticipantId}/roles/{roleId}";
            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
            {
                using var request = new HttpRequestMessage(method, path);
                request.Headers.TryAddWithoutValidation(header, token);
                using var response = await browser.SendAsync(request, ct);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        app.Http.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);
        foreach (var roleId in new[] { TangentBuiltInRoles.Owner.Token, TangentBuiltInRoles.Participant.Token })
        {
            using var response = await app.Http.PutAsync(
                $"/api/identity/roles/{roleId}/members/{app.HumanParticipantId}", null, ct);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Member_pages_preserve_total_and_exact_lookup_reaches_beyond_the_first_page()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var roles = app.Services.GetRequiredService<RoleCollection>();
        const string roleKey = "role:directory-review";
        await roles.Define(roleKey, "Directory review", ct: ct);
        var ids = new List<string>();
        for (var n = 0; n < 51; n++)
        {
            var participant = new Participant { Id = Participant.NewIdentifier() };
            await participant.Save(ct);
            await ParticipantIdentity.Internal(participant.Id, DateTimeOffset.UtcNow).Save(ct);
            await new ParticipantIdentity
            {
                Id = ParticipantIdentity.Key("test", $"member-{n}"), Kind = "test", Value = $"member-{n}",
                ParticipantId = participant.Id, Label = $"member-{n}.test"
            }.Save(ct);
            await roles.Add(roleKey, participant.Id, ct);
            ids.Add(participant.Id);
        }
        var scheme = Koan.Web.Auth.Extensions.AuthenticationExtensions.CookieScheme;
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ParticipationConstants.ParticipantClaim, app.OwnerParticipantId)], "browser"));
        using var browser = new HttpClient { BaseAddress = new Uri(app.Origin) };
        browser.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" +
            options.TicketDataFormat.Protect(new AuthenticationTicket(principal, scheme)));
        async Task<JsonDocument> Read(string suffix)
        {
            using var response = await browser.GetAsync("/api/roles/ui/search?role=" + roleKey + suffix, ct);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        using var first = await Read("");
        using var last = await Read("&page=2");
        Assert.Equal(51, first.RootElement.GetProperty("totalMembers").GetInt32());
        Assert.Equal(50, first.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(2, first.RootElement.GetProperty("nextPage").GetInt32());
        Assert.False(last.RootElement.TryGetProperty("nextPage", out var continuation)
            && continuation.ValueKind != JsonValueKind.Null);
        var returned = first.RootElement.GetProperty("items").EnumerateArray()
            .Concat(last.RootElement.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("id").GetString()).ToArray();
        Assert.Equal(ids.Order(StringComparer.Ordinal), returned);
        using var exact = await Read("&query=%40MEMBER-50.TEST");
        Assert.Equal(ids[50], Assert.Single(exact.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        using var partial = await Read("&query=member");
        Assert.Empty(partial.RootElement.GetProperty("items").EnumerateArray());
    }
}
