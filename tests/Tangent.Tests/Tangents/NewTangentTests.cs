using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Tangents;

/// <summary>Creating a Tangent from <c>/t/new</c>: an administrator names it and lands on it, everything else at its
/// default; it shows on the door and in the switcher; whoever may not create one is offered nothing and refused with
/// the gate; a refused name keeps the form; and the form answers JSON with its defaults, <c>can</c> and <c>gate</c>.</summary>
public sealed class NewTangentTests
{
    private const string Leo = "did:plc:leo4tangents";

    [Fact]
    public async Task An_administrator_creates_a_tangent_with_only_a_name_and_lands_on_it_with_its_slug_kind_audience_and_order_defaulted()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        var before = await Event.Count.Exact(Cancellation);

        var form = await Send(leo.Client, HttpMethod.Get, "/t/new");
        Assert.Equal(HttpStatusCode.OK, form.Status);
        Assert.Contains("id=\"new-tangent-name\"", form.Body);
        Assert.Contains("Discussions · Public · no description", WebUtility.HtmlDecode(form.Body));
        Assert.DoesNotContain("data-gate", form.Body);

        var created = await Form(leo, "/t/new", "/t/new", new() { ["name"] = "The garden" });

        Assert.Equal(HttpStatusCode.SeeOther, created.Status);
        Assert.Equal(BaseUrl + "/t/the-garden", created.Location);
        var row = await Tangent.Get("the-garden", Cancellation);
        Assert.NotNull(row);
        Assert.Equal(("The garden", "", PostKind.Topic, 3), (row.Name, row.Description, row.DefaultKind, row.Order));
        Assert.Equal(Audience.Public, await Overwrites.AudienceOf("the-garden", Cancellation));
        var logged = Assert.Single(await Event.All(Cancellation), e => e.TangentId == "the-garden");
        Assert.Equal((EventKind.TangentChanged, Leo, Audience.Public, before + 1), (logged.Kind, logged.ActorId, logged.Audience, logged.Seq));
        var landed = await Send(leo.Client, HttpMethod.Get, "/t/the-garden");
        Assert.Equal(HttpStatusCode.OK, landed.Status);
        Assert.Contains("<h1 id=\"tangent-title\" class=\"tangent-title\">The garden</h1>", landed.Body);

        // A name another Tangent's slug holds takes a numbered suffix, and no name takes the form's own address.
        Assert.Equal(BaseUrl + "/t/workshop-2", (await Form(leo, "/t/new", "/t/new", new() { ["name"] = "Workshop" })).Location);
        Assert.Equal(BaseUrl + "/t/new-2", (await Form(leo, "/t/new", "/t/new", new() { ["name"] = "New" })).Location);
        Assert.Equal((4, 5), ((await Tangent.Get("workshop-2", Cancellation))!.Order, (await Tangent.Get("new-2", Cancellation))!.Order));
    }

    [Fact]
    public async Task A_new_tangent_shows_on_the_door_and_in_the_switcher_for_a_member_who_is_offered_no_new_tangent_and_is_refused_with_the_gate()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        Assert.Equal(HttpStatusCode.SeeOther, (await Form(leo, "/t/new", "/t/new", new() { ["name"] = "The garden" })).Status);

        var door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Contains("the-garden", Slugs(door.GetProperty("tangents")));
        Assert.False(door.TryGetProperty("newTangent", out _));
        Assert.DoesNotContain("New Tangent", (await Send(ada.Client, HttpMethod.Get, "/")).Body);
        var page = (await Send(ada.Client, HttpMethod.Get, "/t/workshop", json: true)).Json;
        Assert.Contains("the-garden", Slugs(page.GetProperty("tangents")));
        Assert.False(page.TryGetProperty("newTangent", out _));
        var switcher = (await Send(ada.Client, HttpMethod.Get, "/t/workshop")).Body;
        Assert.Contains($"href=\"{BaseUrl}/t/the-garden\"", switcher);
        Assert.DoesNotContain("New Tangent", switcher);

        Assert.Equal(BaseUrl + "/t/new", (await Send(leo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("newTangent").GetString());
        Assert.Contains($"<a class=\"rail-new\" href=\"{BaseUrl}/t/new\">New Tangent</a>", (await Send(leo.Client, HttpMethod.Get, "/")).Body);
        Assert.Equal(BaseUrl + "/t/new", (await Send(leo.Client, HttpMethod.Get, "/t/workshop", json: true)).Json.GetProperty("newTangent").GetString());
        Assert.Contains($"href=\"{BaseUrl}/t/new\">", (await Send(leo.Client, HttpMethod.Get, "/t/workshop")).Body);

        var gate = Capabilities.NoRoleGrants(Bit.ManageSpace);
        var offered = await Send(ada.Client, HttpMethod.Get, "/t/new");
        Assert.Contains($"data-gate=\"{gate}\"", offered.Body);
        Assert.DoesNotContain("type=\"submit\"", offered.Body);
        var refused = await Form(ada, "/t/workshop", "/t/new", new() { ["name"] = "Mine" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.Contains($"data-gate=\"{gate}\"", refused.Body);
        Assert.Contains("value=\"Mine\"", refused.Body);
        var asJson = await Json(ada, "/t/workshop", "/t/new", new { name = "Mine" });
        Assert.Equal((HttpStatusCode.Forbidden, gate), (asJson.Status, asJson.Gate));
        Assert.Null(await Tangent.Get("mine", Cancellation));
        Assert.Equal(Capabilities.SignIn, (await Send(Agent(tangentSpace), HttpMethod.Get, "/t/new", json: true)).Json.GetProperty("gate").GetString());
    }

    [Fact]
    public async Task A_refused_name_keeps_the_form_with_what_was_sent_and_creates_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);

        var empty = await Form(leo, "/t/new", "/t/new", new()
        {
            ["name"] = "   ", ["description"] = "Short pieces from the road.", ["kind"] = "article", ["audience"] = "private"
        });
        var tooLong = new string('a', Tangent.NameLimit + 1);
        var long_ = await Form(leo, "/t/new", "/t/new", new() { ["name"] = tooLong });

        Assert.Equal(HttpStatusCode.BadRequest, empty.Status);
        Assert.Contains($"<p class=\"field-failure\" id=\"new-tangent-name-failure\">{Tangent.NeedsName}</p>", empty.Body);
        Assert.Contains("Short pieces from the road.</textarea>", empty.Body);
        Assert.Contains("value=\"article\" checked", empty.Body);
        Assert.Contains("value=\"private\" checked", empty.Body);
        Assert.Contains("<details class=\"post-form-more\" open>", empty.Body);
        Assert.Equal(HttpStatusCode.BadRequest, long_.Status);
        Assert.Contains("at most 80 characters", long_.Body);
        Assert.Contains($"value=\"{tooLong}\"", long_.Body);
        Assert.Equal(3, await Tangent.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task The_new_tangent_form_answers_json_with_where_to_post_its_defaults_can_and_gate_and_creates_by_json()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);

        var form = (await Send(leo.Client, HttpMethod.Get, "/t/new", json: true)).Json;
        Assert.Equal(BaseUrl + "/t/new", form.GetProperty("create").GetProperty("action").GetString());
        Assert.Equal(["name", "description", "kind", "audience"], FieldNames(form.GetProperty("create")));
        Assert.Contains("ManageSpace", Strings(form.GetProperty("can")));
        Assert.False(form.TryGetProperty("gate", out _));
        Assert.Equal(("", "", "topic", "public"), (form.GetProperty("name").GetString(), form.GetProperty("description").GetString(),
            form.GetProperty("kind").GetString(), form.GetProperty("audience").GetString()));
        Assert.Equal(["topic", "article"], Strings(form.GetProperty("kinds")));
        Assert.Equal(["public", "private"], Strings(form.GetProperty("audiences")));
        var member = (await Send(ada.Client, HttpMethod.Get, "/t/new", json: true)).Json;
        Assert.Equal(Capabilities.NoRoleGrants(Bit.ManageSpace), member.GetProperty("gate").GetString());
        Assert.DoesNotContain("ManageSpace", Strings(member.GetProperty("can")));
        Assert.False(member.TryGetProperty("create", out _));

        var created = await Json(leo, "/t/new", "/t/new", new { name = "Field guides", kind = "article", audience = "private" });

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(BaseUrl + "/t/field-guides", created.Location);
        Assert.Equal(("field-guides", "Field guides", "article", "private", BaseUrl + "/t/field-guides/settings"),
            (created.Json.GetProperty("slug").GetString(), created.Json.GetProperty("name").GetString(), created.Json.GetProperty("kind").GetString(),
                created.Json.GetProperty("audience").GetString(), created.Json.GetProperty("settings").GetString()));
        Assert.Contains("ManageTangent", Strings(created.Json.GetProperty("can")));
        var refused = await Json(leo, "/t/new", "/t/new", new { name = "Field guides", kind = "gallery" });
        Assert.Equal((HttpStatusCode.BadRequest, TangentWrites.KindRule), (refused.Status, refused.Json.GetProperty("detail").GetString()));
    }

    private static List<string?> Slugs(JsonElement tangents) => tangents.EnumerateArray().Select(tangent => tangent.GetProperty("slug").GetString()).ToList();

    private static List<string?> Strings(JsonElement values) => values.EnumerateArray().Select(value => value.GetString()).ToList();
}
