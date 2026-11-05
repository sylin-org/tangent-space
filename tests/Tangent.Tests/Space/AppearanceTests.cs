using System.Net;
using System.Net.Http.Headers;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Space;

/// <summary>The place's appearance at <c>/settings/appearance</c>: whoever holds ManageSpace sets its accent, its base, its
/// palette and its own CSS, as a form or as JSON, appending space.changed, and previews the door in them first; anyone else
/// is refused; and every page, for every reader, carries it, the settings without the place's own CSS.</summary>
public sealed class AppearanceTests
{
    private const string Leo = "did:plc:leo4appearance";
    private const string Path = TangentSpaceSettingsController.AppearancePath;

    private static Dictionary<string, string> Accent(string word) => new() { [TangentSpaceSettingsController.AccentField] = word };

    private static string Carrying(string word) => $"<html lang=\"en\" data-accent=\"{word}\">";

    private static async Task<Accent> Held() => (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Appearance.Accent;

    [Fact]
    public async Task Whoever_holds_manage_space_sets_the_accent_which_appends_space_changed_and_every_page_and_reader_carries_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var visitor = Agent(tangentSpace);

        // A place starts gold, and the settings rail offers the section.
        var page = await Send(leo.Client, HttpMethod.Get, Path, json: true);
        Assert.Equal((HttpStatusCode.OK, "gold"), (page.Status, page.Json.GetProperty("accent").GetString()));
        Assert.Equal(["gold", "jade", "iris", "copper"], page.Json.GetProperty("accents").EnumerateArray().Select(word => word.GetString()));
        Assert.Contains(page.Json.GetProperty("sections").EnumerateArray(),
            section => section.GetProperty("name").GetString() == "Appearance" && section.GetProperty("address").GetString() == BaseUrl + Path && section.GetProperty("current").GetBoolean());
        Assert.False(page.Json.TryGetProperty("gate", out _));
        Assert.Contains(Carrying("gold"), (await Send(visitor, HttpMethod.Get, "/")).Body);
        var held = await Tagged(visitor, "/t/" + Workshop, null);
        var before = await Event.Count.Exact(Cancellation);

        var saved = await Form(leo, Path, Path, Accent("jade"));

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + Path), (saved.Status, saved.Location));
        Assert.Equal(Space.Accent.Jade, await Held());
        var logged = Assert.Single(await Event.All(Cancellation), e => e.Seq > before);
        Assert.Equal((EventKind.SpaceChanged, Leo), (logged.Kind, logged.ActorId));
        Assert.Contains("value=\"jade\" checked", (await Send(leo.Client, HttpMethod.Get, Path)).Body);

        // Every page says it, to whoever reads: the door and a Tangent, a participant and a visitor, the sign-in, and a
        // page a cache already holds, whose own tag no longer answers for it.
        foreach (var path in new[] { "/", "/t/" + Workshop })
        {
            Assert.Contains(Carrying("jade"), (await Send(visitor, HttpMethod.Get, path)).Body);
            Assert.Contains(Carrying("jade"), (await Send(leo.Client, HttpMethod.Get, path)).Body);
        }
        Assert.Contains(Carrying("jade"), (await Send(visitor, HttpMethod.Get, "/sign-in")).Body);
        var revalidated = await Tagged(visitor, "/t/" + Workshop, held.ETag);
        Assert.Equal(HttpStatusCode.OK, revalidated.Status);
        Assert.Contains(Carrying("jade"), revalidated.Body);
        Assert.Equal(HttpStatusCode.NotModified, (await Tagged(visitor, "/t/" + Workshop, revalidated.ETag)).Status);

        // As JSON: another accent, read back; a word outside the four is refused; the same choice writes nothing.
        var iris = await Json(leo, Path, Path, new { accent = "iris" });
        Assert.Equal((HttpStatusCode.OK, "iris"), (iris.Status, iris.Json.GetProperty("accent").GetString()));
        Assert.Equal(before + 2, await Event.Count.Exact(Cancellation));
        var unknown = await Json(leo, Path, Path, new { accent = "teal" });
        Assert.Equal((HttpStatusCode.BadRequest, "invalid"), (unknown.Status, unknown.Word));
        var refusedForm = await Form(leo, Path, Path, Accent("teal"));
        Assert.Equal(HttpStatusCode.BadRequest, refusedForm.Status);
        Assert.Contains("id=\"space-appearance-failure\"", refusedForm.Body);
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, Path, Path, new { accent = "iris" })).Status);
        Assert.Equal(Space.Accent.Iris, await Held());
        Assert.Equal(before + 2, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task Anyone_who_does_not_hold_manage_space_is_refused_and_the_accent_stays()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        const string mod = "did:plc:mo4appearance";
        await Given.AParticipant(tangentSpace, mod, [Roles.Member, Roles.Moderator], ct: Cancellation);
        using var mo = await SignedIn(tangentSpace, mod);
        using var visitor = Agent(tangentSpace);
        var gate = Capabilities.NoRoleGrants(Bit.ManageSpace).Word;
        var before = await Event.Count.Exact(Cancellation);

        // A member reads the choices, disabled, under the gate, and has nothing to send.
        var read = await Send(ada.Client, HttpMethod.Get, Path);
        Assert.Contains($"data-gate=\"{gate}\"", read.Body);
        Assert.DoesNotContain("type=\"submit\"", read.Body);
        Assert.DoesNotContain("aria-label=\"Settings\"", read.Body);

        // The settings offer Ada no form, so her session's token comes from the workshop's page, which carries one.
        var form = await Form(ada, "/t/" + Workshop, Path, Accent("jade"));
        Assert.Equal(HttpStatusCode.Forbidden, form.Status);
        Assert.Contains($"data-gate=\"{gate}\"", form.Body);
        var json = await Json(ada, "/t/" + Workshop, Path, new { accent = "jade" });
        Assert.Equal((HttpStatusCode.Forbidden, gate), (json.Status, json.Word));

        // A moderator lands on what they may use, and is refused the same.
        Assert.Equal(BaseUrl + ApplicationsController.Path, (await Send(mo.Client, HttpMethod.Get, Path)).Location);
        Assert.Equal((HttpStatusCode.Forbidden, gate), Refused(await Json(mo, "/t/" + Workshop, Path, new { accent = "jade" })));

        var posted = await Send(visitor, HttpMethod.Post, Path, JsonBody(new { accent = "jade" }), json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word), (posted.Status, posted.Word));

        Assert.Equal(Space.Accent.Gold, await Held());
        Assert.Equal(before, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task The_place_fills_its_layers_with_a_base_a_palette_and_its_own_css_which_every_page_but_the_settings_carries()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var visitor = Agent(tangentSpace);

        // Every layer at its default: Tangent's own base and palette around its components, and no owner layer.
        var door = (await Send(visitor, HttpMethod.Get, "/")).Body;
        Assert.Contains($"href=\"{BaseUrl}/base.css\"", door);
        Assert.Contains($"href=\"{BaseUrl}/tangent.css\"", door);
        Assert.Contains($"href=\"{BaseUrl}/palette.css\"", door);
        Assert.DoesNotContain("@layer owner", door);
        var page = await Send(leo.Client, HttpMethod.Get, Path, json: true);
        Assert.Equal(("tangent", "base", ""), (page.Json.GetProperty("base").GetString(), page.Json.GetProperty("palette").GetString(), page.Json.GetProperty("css").GetString()));
        Assert.Equal(["tangent", "simple", "mvp", "marx", "sakura", "water", "new", "concord"], page.Json.GetProperty("bases").EnumerateArray().Select(word => word.GetString()));

        // Sakura, in its own palette, under the place's own CSS, which stays CSS whatever it holds.
        var sakura = await Json(leo, Path, Path, new { @base = "sakura", css = "article.post.by-agent > header { color: teal; } </style>" });
        Assert.Equal((HttpStatusCode.OK, "sakura", "base"), (sakura.Status, sakura.Json.GetProperty("base").GetString(), sakura.Json.GetProperty("palette").GetString()));
        foreach (var body in new[] { (await Send(visitor, HttpMethod.Get, "/")).Body, (await Send(leo.Client, HttpMethod.Get, "/t/" + Workshop)).Body })
        {
            Assert.Contains($"href=\"{BaseUrl}/bases/sakura.css\"", body);
            Assert.Contains($"href=\"{BaseUrl}/bases/sakura.palette.css\"", body);
            Assert.Contains("<style>@layer owner {\narticle.post.by-agent > header { color: teal; } <\\/style>\n}</style>", body);
        }

        // The settings wear the place's base and palette and leave its own CSS out, so it can hide no way back; so does a
        // refusal answered under them.
        var settings = (await Send(leo.Client, HttpMethod.Get, Path)).Body;
        Assert.Contains($"href=\"{BaseUrl}/bases/sakura.css\"", settings);
        Assert.DoesNotContain("@layer owner", settings);
        var refusal = await Send(leo.Client, HttpMethod.Get, "/settings/export?after=garbage");
        Assert.True((int)refusal.Status >= 400, refusal.Status.ToString());
        Assert.Contains("class=\"masthead", refusal.Body);
        Assert.DoesNotContain("@layer owner", refusal.Body);

        // Tangent's palette over the base; a stylesheet Tangent does not bundle is refused.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, Path, Path, new { palette = "accent" })).Status);
        Assert.Contains($"href=\"{BaseUrl}/palette.css\"", (await Send(visitor, HttpMethod.Get, "/")).Body);
        Assert.Equal(HttpStatusCode.BadRequest, (await Json(leo, Path, Path, new { @base = "bootstrap" })).Status);

        // The preview draws the door in what it is sent and saves nothing.
        var preview = await Form(leo, Path, TangentSpaceSettingsController.PreviewPath, new() { [TangentSpaceSettingsController.BaseField] = "water", [TangentSpaceSettingsController.PaletteField] = "base" });
        Assert.Equal(HttpStatusCode.OK, preview.Status);
        Assert.Contains($"href=\"{BaseUrl}/bases/water.css\"", preview.Body);
        Assert.Contains($"href=\"{BaseUrl}/bases/water.palette.css\"", preview.Body);
        Assert.Equal(Space.Base.Sakura, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Appearance.Base);

        // Each layer returns to its default by sending it.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, Path, Path, new { @base = "tangent", palette = "base", css = "" })).Status);
        door = (await Send(visitor, HttpMethod.Get, "/")).Body;
        Assert.Contains($"href=\"{BaseUrl}/base.css\"", door);
        Assert.Contains($"href=\"{BaseUrl}/palette.css\"", door);
        Assert.DoesNotContain("@layer owner", door);
    }

    private static (HttpStatusCode, string?) Refused(Answered answered) => (answered.Status, answered.Word);

    /// <summary>A page as a browser that holds <paramref name="held"/> revalidates it.</summary>
    private static async Task<(HttpStatusCode Status, EntityTagHeaderValue? ETag, string Body)> Tagged(HttpClient client, string path, EntityTagHeaderValue? held)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        if (held is not null) request.Headers.IfNoneMatch.Add(held);
        using var response = await client.SendAsync(request, Cancellation);
        return (response.StatusCode, response.Headers.ETag, await response.Content.ReadAsStringAsync(Cancellation));
    }
}
