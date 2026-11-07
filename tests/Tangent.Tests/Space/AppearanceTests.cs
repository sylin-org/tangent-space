using System.Net;
using System.Net.Http.Headers;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Space;

/// <summary>The place's appearance at <c>/settings/appearance</c>: whoever holds ManageSpace sets its colours, its base, its
/// own CSS, its image and its words, as a form or as JSON, appending space.changed, and previews the door in them first;
/// anyone else is refused; and every page, for every reader, carries it, the settings without the place's own CSS.</summary>
public sealed class AppearanceTests
{
    private static readonly string Leo = Persona.Id("leo4appearance");
    private const string Path = TangentSpaceSettingsController.AppearancePath;

    private static Dictionary<string, string> Colours(string word) => new() { [TangentSpaceSettingsController.PaletteField] = word };

    private static string Carrying(string word) => $"<html lang=\"en\" data-accent=\"{word}\">";

    private static async Task<Palette> Held() => (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Appearance.Palette;

    [Fact]
    public async Task Whoever_holds_manage_space_sets_the_colours_which_appends_space_changed_and_every_page_and_reader_carries_them()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var visitor = Agent(tangentSpace);

        // A place starts in the base's own colours, Tangent's palette in gold, and the settings rail offers the section.
        var page = await Send(leo.Client, HttpMethod.Get, Path, json: true);
        Assert.Equal((HttpStatusCode.OK, "base"), (page.Status, page.Json.GetProperty("palette").GetString()));
        Assert.Equal(["base", "gold", "jade", "iris", "copper"], page.Json.GetProperty("palettes").EnumerateArray().Select(word => word.GetString()));
        Assert.Contains(page.Json.GetProperty("sections").EnumerateArray(),
            section => section.GetProperty("name").GetString() == "Appearance" && section.GetProperty("address").GetString() == BaseUrl + Path && section.GetProperty("current").GetBoolean());
        Assert.False(page.Json.TryGetProperty("gate", out _));
        Assert.Contains(Carrying("gold"), (await Send(visitor, HttpMethod.Get, "/")).Body);
        var held = await Tagged(visitor, "/t/" + Workshop, null);
        var before = await Event.Count.Exact(Cancellation);

        var saved = await Form(leo, Path, Path, Colours("jade"));

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + Path), (saved.Status, saved.Location));
        Assert.Equal(Palette.Jade, await Held());
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

        // As JSON: another accent, read back; a word outside the five is refused; the same choice writes nothing.
        var iris = await Json(leo, Path, Path, new { palette = "iris" });
        Assert.Equal((HttpStatusCode.OK, "iris"), (iris.Status, iris.Json.GetProperty("palette").GetString()));
        Assert.Equal(before + 2, await Event.Count.Exact(Cancellation));
        var unknown = await Json(leo, Path, Path, new { palette = "teal" });
        Assert.Equal((HttpStatusCode.BadRequest, "invalid"), (unknown.Status, unknown.Word));
        var refusedForm = await Form(leo, Path, Path, Colours("teal"));
        Assert.Equal(HttpStatusCode.BadRequest, refusedForm.Status);
        Assert.Contains("id=\"space-appearance-failure\"", refusedForm.Body);
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, Path, Path, new { palette = "iris" })).Status);
        Assert.Equal(Palette.Iris, await Held());
        Assert.Equal(before + 2, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task Anyone_who_does_not_hold_manage_space_is_refused_and_the_colours_stay()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var mod = Persona.Id("mo4appearance");
        await Given.AParticipant(tangentSpace, mod, [Roles.Member, Roles.Moderator], ct: Cancellation);
        using var mo = await SignedIn(tangentSpace, mod);
        using var visitor = Agent(tangentSpace);
        var gate = Capabilities.NoRoleGrants(Bit.ManageSpace).Word;
        var before = await Event.Count.Exact(Cancellation);

        // A member reads the choices, disabled, under the gate, and has nothing to send.
        var read = await Send(ada.Client, HttpMethod.Get, Path);
        Assert.Contains($"data-gate=\"{gate}\"", read.Body);
        Assert.DoesNotContain("type=\"submit\"", PageContent(read.Body));
        Assert.DoesNotContain("aria-label=\"Settings\"", read.Body);

        // The settings offer Ada no form, so her session's token comes from the workshop's page, which carries one.
        var form = await Form(ada, "/t/" + Workshop, Path, Colours("jade"));
        Assert.Equal(HttpStatusCode.Forbidden, form.Status);
        Assert.Contains($"data-gate=\"{gate}\"", form.Body);
        var json = await Json(ada, "/t/" + Workshop, Path, new { palette = "jade" });
        Assert.Equal((HttpStatusCode.Forbidden, gate), (json.Status, json.Word));

        // A moderator lands on what they may use, and is refused the same.
        Assert.Equal(ApplicationsController.WaitingAddress(BaseUrl), (await Send(mo.Client, HttpMethod.Get, Path)).Location);
        Assert.Equal((HttpStatusCode.Forbidden, gate), Refused(await Json(mo, "/t/" + Workshop, Path, new { palette = "jade" })));

        var posted = await Send(visitor, HttpMethod.Post, Path, JsonBody(new { palette = "jade" }), json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word), (posted.Status, posted.Word));

        Assert.Equal(Palette.Base, await Held());
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
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, Path, Path, new { palette = "copper" })).Status);
        Assert.Contains($"href=\"{BaseUrl}/palette.css\"", (await Send(visitor, HttpMethod.Get, "/")).Body);
        Assert.Equal(HttpStatusCode.BadRequest, (await Json(leo, Path, Path, new { @base = "bootstrap" })).Status);

        // The preview draws the door in what it is sent and saves nothing.
        var preview = await Form(leo, Path, AppearancePreviewController.Path, new() { [TangentSpaceSettingsController.BaseField] = "water", [TangentSpaceSettingsController.PaletteField] = "base" });
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

    [Fact]
    public async Task The_place_wears_its_image_and_its_words_on_the_door_and_its_lines_on_every_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var visitor = Agent(tangentSpace);

        var dressed = await Json(leo, Path, Path, new
        {
            image = "greenhouse",
            description = "A workshop for people who make things *by hand*.",
            intro = "## Welcome\n\nRead anything; say hello before you post.",
            masthead = "[Our site](https://sylin.org/)",
            footer = "[Rules](https://sylin.org/rules)"
        });
        Assert.Equal((HttpStatusCode.OK, "greenhouse"), (dressed.Status, dressed.Json.GetProperty("image").GetString()));

        var door = (await Send(visitor, HttpMethod.Get, "/")).Body;
        Assert.Contains($"<img class=\"head-image\" src=\"{BaseUrl}/art/greenhouse.webp\"", door);
        Assert.DoesNotContain("<link rel=\"icon\"", door);
        Assert.Contains("make things <em>by hand</em>.", door);
        Assert.Contains("<h2", door);
        Assert.Contains("<div class=\"masthead-line\"><p><a href=\"https://sylin.org/\"", door);
        Assert.Contains("<div class=\"footer-line\">", door);
        Assert.Contains("<div class=\"masthead-line\">", (await Send(leo.Client, HttpMethod.Get, Path)).Body);

        // The place's mark is its letter until an image brought in here stands in for it, beside its name and as every page's icon.
        var mark = await Media.Keep(ArticleMediaTests.Jpeg(), Cancellation);
        await new BringMedia(mark.Id, Leo).Send(Cancellation);
        var marked = await Json(leo, Path, Path, new { mark = Media.Reference(mark.Id) });
        Assert.Equal((HttpStatusCode.OK, Media.Reference(mark.Id)), (marked.Status, marked.Json.GetProperty("mark").GetString()));
        door = (await Send(visitor, HttpMethod.Get, "/")).Body;
        Assert.Contains($"<link rel=\"icon\" href=\"{BaseUrl}/media/{mark.Id}\"", door);
        Assert.Contains($"<img class=\"masthead-mark-picture\" src=\"{BaseUrl}/media/{mark.Id}\"", door);

        // A cover Tangent does not bundle is refused, and nothing changes.
        Assert.Equal(HttpStatusCode.BadRequest, (await Json(leo, Path, Path, new { image = "elsewhere" })).Status);
        Assert.Equal("greenhouse", (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Appearance.Image);
    }

    [Fact]
    public async Task An_agent_brings_in_the_banner_and_the_picture_its_bluesky_account_shows_as_the_places_image_and_mark_through_the_write_a_press_sends()
    {
        var pictures = new Pictures();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddHttpClient<ProfileReader>().ConfigurePrimaryHttpMessageHandler(() => pictures), Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        async Task Shows(string? banner, string? avatar)
        {
            var leo = (await Participant.Get(Leo, Cancellation))!;
            leo.Profile = new Profile { Banner = banner, Avatar = avatar, CapturedAt = DateTimeOffset.UtcNow };
            Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(leo).Send(Cancellation));
        }
        await Shows(Pictures.Banner, Pictures.Avatar);
        using var browser = await SignedIn(tangentSpace, Leo);
        using var agent = await Tools.ToolClient.Connect(tangentSpace, browser);

        // First steps offer each as change_appearance's own field, which a press and an agent send alike.
        var steps = (await Send(browser.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("firstSteps");
        Assert.Equal((BaseUrl + Path, TangentSpaceSettingsController.Bluesky),
            (steps.GetProperty("banner").GetProperty("action").GetString(), Valued(steps.GetProperty("banner"), TangentSpaceSettingsController.ImageField)));
        Assert.Equal((BaseUrl + Path, TangentSpaceSettingsController.Bluesky),
            (steps.GetProperty("mark").GetProperty("action").GetString(), Valued(steps.GetProperty("mark"), TangentSpaceSettingsController.MarkField)));

        var called = await agent.Call("change_appearance", new { image = "bluesky", mark = "bluesky" });
        Assert.False(called.Failed, called.Text);
        var (image, mark) = (called.Structured!.Value.GetProperty("image").GetString()!, called.Structured!.Value.GetProperty("mark").GetString()!);
        Assert.NotNull(Media.IdIn(image));
        Assert.NotNull(Media.IdIn(mark));
        Assert.NotEqual(image, mark);
        Assert.Equal([Pictures.Banner, Pictures.Avatar], pictures.Asked);
        var worn = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Appearance;
        Assert.Equal((image, mark), (worn.Image, worn.Mark));
        var palette = worn.Palette;
        Assert.Contains($"<link rel=\"icon\" href=\"{BaseUrl}{mark}\"", (await Send(Agent(tangentSpace), HttpMethod.Get, "/")).Body);

        // A person's press on the door sends the same write and comes back to the door.
        var pressed = await Form(browser, "/", Path, new() { [TangentSpaceSettingsController.ImageField] = TangentSpaceSettingsController.Bluesky, [PostWrites.ReturnField] = "/" });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/"), (pressed.Status, pressed.Location));
        Assert.Equal(3, pictures.Asked.Count);

        // An account that showed no banner says so, and one whose picture does not arrive says to try again; nothing changes.
        await Shows(null, Pictures.Avatar);
        var none = await agent.Call("change_appearance", new { image = "bluesky", palette = "jade" });
        Assert.True(none.Failed);
        Assert.Contains(TangentSpaceSettingsController.NoneOnBluesky(mark: false).Text, none.Text);
        pictures.Down = true;
        var down = await agent.Call("change_appearance", new { mark = "bluesky" });
        Assert.True(down.Failed);
        Assert.Contains(TangentSpaceSettingsController.NoBlueskyPicture.Text, down.Text);
        var after = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Appearance;
        Assert.Equal((image, mark, palette), (after.Image, after.Mark, after.Palette));

        // Whoever may not change the appearance is refused with the gate, and nothing of theirs is read.
        using var ada = await SignedIn(tangentSpace, Ada);
        using var hers = await Tools.ToolClient.Connect(tangentSpace, ada);
        var refused = await hers.Call("change_appearance", new { image = "bluesky" });
        Assert.True(refused.Failed);
        Assert.Equal(3, pictures.Asked.Count);
    }

    /// <summary>Where the pictures a profile names are read: the banner and the avatar answer an image each, recorded as
    /// they are asked, until <see cref="Down"/>; anything else, and every profile read, finds nothing.</summary>
    private sealed class Pictures : HttpMessageHandler
    {
        public const string Banner = "https://cdn.bsky.example/banner/leo.jpg";
        public const string Avatar = "https://cdn.bsky.example/avatar/leo.jpg";

        public System.Collections.Concurrent.ConcurrentQueue<string> Asked { get; } = new();

        public bool Down { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var address = request.RequestUri!.AbsoluteUri;
            if (Down || address is not (Banner or Avatar)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            Asked.Enqueue(address);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(ArticleMediaTests.Jpeg(address == Banner ? 96 : 64, secrets: false))
            });
        }
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
