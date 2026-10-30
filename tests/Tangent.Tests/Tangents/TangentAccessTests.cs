using System.Net;
using System.Text.Json;
using Tangent.Access;
using Tangent.Posts;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Tangents;

/// <summary>A Tangent's access at <c>/t/{tangent}/settings/access</c>, for whoever may manage it: beyond its audience,
/// each role reads and posts there as it does in the place, reads alone, or reads and posts, as the role's overwrite. A
/// private Tangent opened to the member role is a members-only Tangent: a member reads it and starts Topics there, and a
/// visitor finds nothing there.</summary>
public sealed class TangentAccessTests
{
    private const string Leo = "did:plc:leo4access";

    private static string AccessOf(string tangent) => "/t/" + tangent + "/settings/access";

    /// <summary>What the access page lists for the role with <paramref name="word"/>.</summary>
    private static JsonElement Row(JsonElement page, string word)
        => page.GetProperty("roles").EnumerateArray().Single(role => role.GetProperty("word").GetString() == word);

    [Fact]
    public async Task A_private_tangent_opened_to_the_member_role_is_read_and_posted_in_by_a_member_and_absent_to_a_visitor()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Member, Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var visitor = Agent(tangentSpace);
        var access = AccessOf(Studio);

        // Private: only administrators read it, and every role holds there what it holds in the place.
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio)).Status);
        var page = await Send(leo.Client, HttpMethod.Get, access, json: true);
        Assert.Equal((HttpStatusCode.OK, "private"), (page.Status, page.Json.GetProperty("audience").GetString()));
        Assert.Equal(["moderator", "member"], page.Json.GetProperty("roles").EnumerateArray().Select(role => role.GetProperty("word").GetString()));
        Assert.Equal("default", Row(page.Json, "member").GetProperty("access").GetString());
        Assert.Equal(["read", "post"], Row(page.Json, "member").GetProperty("offered").EnumerateArray().Select(word => word.GetString()));
        Assert.Contains($"href=\"{BaseUrl}{access}\"", (await Send(leo.Client, HttpMethod.Get, "/t/" + Studio + "/settings")).Body);

        // Opened to members from the page's form: a member reads it and starts a Topic there.
        var opened = await Form(leo, access, access, new Dictionary<string, string>
        {
            [TangentAccessController.RoleField] = "member", [TangentAccessController.AccessField] = "post"
        });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + access), (opened.Status, opened.Location));
        Assert.Equal(RoleAccess.Post, Overwrites.AccessOf(await Overwrites.Of(Studio, Roles.Member, Cancellation)));
        Assert.Equal(HttpStatusCode.OK, (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio)).Status);
        var started = await Json(ada, "/t/" + Studio, "/t/" + Studio + "/new", new { id = Tids.Mint(), title = "Members only", body = "Just us here." });
        Assert.Equal(HttpStatusCode.Created, started.Status);

        // A visitor finds nothing there, as at an address with nothing.
        var absent = await Send(visitor, HttpMethod.Get, "/t/" + Studio, json: true);
        Assert.Equal((HttpStatusCode.NotFound, Capabilities.Absent), (absent.Status, absent.Gate));
        Assert.Equal((await Send(visitor, HttpMethod.Get, "/t/nowhere", json: true)).Body, absent.Body);
        Assert.DoesNotContain("Members only", (await Send(visitor, HttpMethod.Get, "/t/" + Studio)).Body);

        // Read alone, as JSON: she reads, and starts nothing.
        var reading = await Json(leo, access, access, new { role = "member", access = "read" });
        Assert.Equal((HttpStatusCode.OK, "read"), (reading.Status, reading.Json.GetProperty("access").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio)).Status);
        Assert.DoesNotContain(nameof(Bit.Topic), (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio, json: true)).Json
            .GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));

        // A member who reads the Tangent reads the gate on its access, and no role.
        var unmanaged = await Send(ada.Client, HttpMethod.Get, access, json: true);
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageTangent)), (unmanaged.Status, unmanaged.Json.GetProperty("gate").GetString()));
        Assert.Empty(unmanaged.Json.GetProperty("roles").EnumerateArray());

        // A member manages nothing here; a role no one has is not found; an administrator's access is not set.
        var refused = await Json(ada, "/t/" + Studio, access, new { role = "member", access = "post" });
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageTangent)), (refused.Status, refused.Gate));
        Assert.Equal(HttpStatusCode.NotFound, (await Json(leo, access, access, new { role = "nobody", access = "post" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Json(leo, access, access, new { role = "administrator", access = "read" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Json(leo, access, access, new { role = "member", access = "everything" })).Status);

        // Back to as in the place: private again to her.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, access, access, new { role = "member", access = "default" })).Status);
        Assert.Null(await Overwrites.Of(Studio, Roles.Member, Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio)).Status);
    }
}
