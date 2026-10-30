using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Koan.Data.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>An article's images: brought in the page or by an agent, kept re-encoded and without their metadata, used as
/// a cover or in a body, and shown only to whoever may read a Post that uses them.</summary>
public sealed class ArticleMediaTests
{
    private const string Secrets = "SecretCamera|Secret Artist|Secret-Byline|Secret-City|Secret-Xmp|Secret-Png-Text";

    private static byte[] Jpeg(int size = 64, bool secrets = true, Color? colour = null)
    {
        using var image = new Image<Rgba32>(size, size, colour ?? Color.Teal);
        if (secrets)
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Make, "SecretCamera");
            image.Metadata.ExifProfile.SetValue(ExifTag.Artist, "Secret Artist");
            image.Metadata.IptcProfile = new IptcProfile();
            image.Metadata.IptcProfile.SetValue(IptcTag.Byline, "Secret-Byline");
            image.Metadata.IptcProfile.SetValue(IptcTag.City, "Secret-City");
            image.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><a>Secret-Xmp</a></x:xmpmeta>"));
        }

        using var bytes = new MemoryStream();
        image.SaveAsJpeg(bytes);
        return bytes.ToArray();
    }

    private static byte[] PngWithText()
    {
        using var image = new Image<Rgba32>(32, 32, Color.Coral);
        image.Metadata.GetPngMetadata().TextData.Add(new PngTextData("Comment", "Secret-Png-Text", "", ""));
        using var bytes = new MemoryStream();
        image.SaveAsPng(bytes);
        return bytes.ToArray();
    }

    /// <summary>Brings an image in as the page does: a multipart form with the page's token, answered as JSON.</summary>
    private static async Task<Answered> Bring(Browser browser, byte[] image, string tangent = Articles, string page = "/t/" + Articles + "/new")
    {
        var fields = new MultipartFormDataContent();
        foreach (var (name, value) in await browser.Token(page, Cancellation)) fields.Add(new StringContent(value), name);
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        fields.Add(file, NewMediaController.FileField, "holiday.jpg");
        return await Send(browser.Client, HttpMethod.Post, "/t/" + tangent + "/" + NewMediaController.Segment, fields, json: true);
    }

    private static async Task<byte[]> Read(HttpClient client, string address)
    {
        using var response = await client.GetAsync(address.Replace(BaseUrl, ""), Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync(Cancellation);
    }

    private static string IdOf(Answered brought) => brought.Json.GetProperty("id").GetString()!;

    [Fact]
    public async Task An_image_comes_in_on_the_page_and_a_cover_leads_the_article_where_it_is_read_and_on_its_list()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);

        var brought = await Bring(ada, Jpeg());
        Assert.Equal(HttpStatusCode.Created, brought.Status);
        var id = IdOf(brought);
        var reference = brought.Json.GetProperty("reference").GetString()!;
        Assert.Equal("/media/" + id, reference);
        Assert.Equal(BaseUrl + reference, brought.Json.GetProperty("address").GetString());

        var page = await Send(ada.Client, HttpMethod.Get, "/t/" + Articles + "/new");
        var written = await Form(ada, "/t/" + Articles + "/new", "/t/" + Articles + "/new", new()
        {
            [PostWrites.IdField] = MintedId(page.Body),
            [PostWrites.TitleField] = "On wicks\nand wax",
            [PostWrites.BodyField] = "## Cotton\n\n![A wick](" + reference + ")\n\nIt burns even.",
            [PostWrites.CoverField] = id
        });
        Assert.Equal(HttpStatusCode.SeeOther, written.Status);

        var read = await Send(ada.Client, HttpMethod.Get, written.Location!.Replace(BaseUrl, ""));
        var cover = read.Body.IndexOf("class=\"article-figure\"", StringComparison.Ordinal);
        var title = read.Body.IndexOf("<h1 class=\"article-title\">On wicks and wax", StringComparison.Ordinal);
        var byline = read.Body.IndexOf("class=\"article-byline\"", StringComparison.Ordinal);
        var body = read.Body.IndexOf("class=\"article-prose\"", StringComparison.Ordinal);
        Assert.True(cover >= 0 && cover < title && title < byline && byline < body, "An article opens with its cover, title and byline, then its body.");
        Assert.Contains("src=\"" + BaseUrl + reference + "\"", read.Body[body..]);

        var list = await Send(ada.Client, HttpMethod.Get, "/t/" + Articles);
        Assert.Contains("src=\"" + BaseUrl + reference + "/" + MediaRecipes.CardName + "\"", list.Body);
        var card = await Read(Agent(tangentSpace), reference + "/" + MediaRecipes.CardName);
        Assert.Equal(64, Image.Identify(card).Width);
    }

    [Fact]
    public async Task An_agent_does_all_of_it_through_the_forms_the_pages_carry()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var bearer = await Delegate(tangentSpace, ada);
        var agent = Agent(tangentSpace);

        var page = (await Send(agent, HttpMethod.Get, "/t/" + Articles + "/new", json: true, bearer: bearer)).Json;
        var brought = await Follow(agent, page.GetProperty("bring"), new Dictionary<string, string> { [NewMediaController.FileField] = Convert.ToBase64String(Jpeg()) }, bearer);
        Assert.Equal(HttpStatusCode.Created, brought.Status);
        var id = IdOf(brought);

        var started = await Follow(agent, page.GetProperty("start"), new Dictionary<string, string>
        {
            [PostWrites.TitleField] = "Field notes",
            [PostWrites.BodyField] = "![A wick](" + brought.Json.GetProperty("reference").GetString() + ")",
            [PostWrites.CoverField] = id
        }, bearer);
        Assert.Equal(HttpStatusCode.Created, started.Status);
        Assert.Equal(id, started.Json.GetProperty("coverId").GetString());

        var topic = (await Send(agent, HttpMethod.Get, started.Location!.Replace(BaseUrl, ""), json: true, bearer: bearer)).Json;
        Assert.Equal(BaseUrl + "/media/" + id, topic.GetProperty("cover").GetString());

        var edit = (await Send(agent, HttpMethod.Get, started.Location!.Replace(BaseUrl, "") + "/edit", json: true, bearer: bearer)).Json;
        Assert.Equal(id, Valued(edit.GetProperty("edit"), PostWrites.CoverField));
        var edited = await Follow(agent, edit.GetProperty("edit"), new Dictionary<string, string> { [PostWrites.BodyField] = "Words alone.", [PostWrites.CoverField] = "" }, bearer);
        Assert.Equal(HttpStatusCode.OK, edited.Status);
        Assert.False(edited.Json.TryGetProperty("coverId", out var left) && left.ValueKind == System.Text.Json.JsonValueKind.String);

        // Nothing in the article uses it now: it reaches the one who brought it, and nobody else.
        Assert.Equal(HttpStatusCode.NotFound, (await Send(agent, HttpMethod.Get, "/media/" + id)).Status);
    }

    [Fact]
    public async Task An_image_in_a_private_tangent_is_absent_to_a_reader_who_cannot_view_it_and_stays_so_for_a_render_or_a_validator()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, "did:plc:leo4writes", [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, "did:plc:leo4writes");
        using var bea = await SignedIn(tangentSpace, Bea);

        var brought = await Bring(leo, Jpeg(), Studio, "/t/" + Studio + "/new");
        var id = IdOf(brought);
        var reference = "/media/" + id;
        var post = await Started(writer, "did:plc:leo4writes", "Plans", "![The plan](" + reference + ")", Studio);
        Assert.Contains(id, post.MediaIds);

        // Whoever may read the Tangent reads the image; nobody else finds anything there, as at an Id nothing has.
        Assert.Equal(HttpStatusCode.OK, (await Send(leo.Client, HttpMethod.Get, reference)).Status);
        var nothing = await Send(Agent(tangentSpace), HttpMethod.Get, "/media/019a0000-0000-7000-8000-000000000000");
        var visitor = await Send(Agent(tangentSpace), HttpMethod.Get, reference);
        var member = await Send(bea.Client, HttpMethod.Get, reference);
        Assert.Equal(HttpStatusCode.NotFound, visitor.Status);
        Assert.Equal(HttpStatusCode.NotFound, member.Status);
        Assert.Equal(nothing.Body.Replace("019a0000-0000-7000-8000-000000000000", id), visitor.Body);

        // Neither a warm render nor a validator passes the gate, and neither answer may be stored by a proxy.
        Assert.Equal(HttpStatusCode.OK, (await Send(leo.Client, HttpMethod.Get, reference + "/" + MediaRecipes.CardName)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(bea.Client, HttpMethod.Get, reference + "/" + MediaRecipes.CardName)).Status);
        using var again = new HttpRequestMessage(HttpMethod.Get, reference);
        using var first = await leo.Client.GetAsync(reference, Cancellation);
        Assert.Equal("private", first.Headers.CacheControl?.Private == true ? "private" : "public");
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        using var notModified = await bea.Client.SendAsync(again, Cancellation);
        Assert.Equal(HttpStatusCode.NotFound, notModified.StatusCode);

        // Naming the image in a public Post is no way to reach it.
        var naming = await Started(writer, Bea, "Look", "![Mine now](" + reference + ")");
        Assert.False(await MediaUse.Reaches(Bea, id, Cancellation));
        Assert.Null(await MediaUse.Get(MediaUse.Key(id, naming.Id), Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(bea.Client, HttpMethod.Get, reference)).Status);
        var stolen = await writer.Send(new StartTopic(Tids.Mint(), Articles, "Mine", "Words.", Bea, CoverId: id), Cancellation);
        Assert.Equal(Post.NoCover, Assert.IsType<Outcome<Post>.Refused>(stolen).Reason);
    }

    [Fact]
    public async Task In_a_private_place_an_image_is_absent_to_a_visitor()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var id = IdOf(await Bring(ada, Jpeg()));
        await Started(writer, Ada, "Open", "![Open](/media/" + id + ")", Articles);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        space!.Audience = Audience.Private;
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Store<TangentSpace>(space), Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, (await Send(Agent(tangentSpace), HttpMethod.Get, "/media/" + id)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(ada.Client, HttpMethod.Get, "/media/" + id)).Status);
    }

    [Fact]
    public async Task What_is_kept_is_re_encoded_with_every_kind_of_metadata_gone_and_the_same_picture_is_kept_once()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);

        foreach (var sent in new[] { Jpeg(), PngWithText() })
        {
            Assert.Matches(Secrets, Encoding.Latin1.GetString(sent));
            var brought = await Bring(ada, sent);
            Assert.Equal(HttpStatusCode.Created, brought.Status);
            var kept = await Read(ada.Client, "/media/" + IdOf(brought));
            Assert.DoesNotMatch(Secrets, Encoding.Latin1.GetString(kept));
            var info = Image.Identify(kept);
            Assert.Null(info.Metadata.ExifProfile);
            Assert.Null(info.Metadata.IptcProfile);
            Assert.Null(info.Metadata.XmpProfile);
        }

        // The same picture brought by two people is one Media, which reaches each of them.
        var mine = await Bring(ada, Jpeg(48));
        var theirs = await Bring(bea, Jpeg(48));
        Assert.Equal(IdOf(mine), IdOf(theirs));
        Assert.Equal((await Read(ada.Client, "/media/" + IdOf(mine))).Length, (await Read(bea.Client, "/media/" + IdOf(theirs))).Length);
    }

    [Fact]
    public async Task A_picture_larger_than_the_kept_edge_is_fitted_and_what_is_not_an_image_is_refused()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);

        var large = await Bring(ada, Jpeg(Media.LongestEdge + 100, secrets: false));
        Assert.Equal(Media.LongestEdge, Image.Identify(await Read(ada.Client, "/media/" + IdOf(large))).Width);

        var svg = await Bring(ada, Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>1</script></svg>"));
        Assert.Equal((HttpStatusCode.BadRequest, Media.NotAnImage), (svg.Status, svg.Json.GetProperty("detail").GetString()));
        var broken = await Bring(ada, [0xFF, 0xD8, 0xFF, 0x00, 0x01]);
        Assert.Equal((HttpStatusCode.BadRequest, Media.NotAnImage), (broken.Status, broken.Json.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Only_someone_who_may_write_there_brings_an_image_in()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var bea = await SignedIn(tangentSpace, Bea);
        var token = (await bea.Token("/t/" + Articles + "/new", Cancellation)).Values.Single();

        var visitor = await Send(Agent(tangentSpace), HttpMethod.Post, "/t/" + Articles + "/media", JsonBody(new { file = Convert.ToBase64String(Jpeg()) }), json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (visitor.Status, visitor.Gate));
        var hidden = await Send(bea.Client, HttpMethod.Post, "/t/" + Studio + "/media", JsonBody(new { file = Convert.ToBase64String(Jpeg()) }), json: true, token: token);
        Assert.Equal((HttpStatusCode.NotFound, Capabilities.Absent), (hidden.Status, hidden.Gate));
        var discussion = await Send(bea.Client, HttpMethod.Get, "/t/" + Workshop + "/new", json: true);
        Assert.False(discussion.Json.TryGetProperty("bring", out var bring) && bring.ValueKind == System.Text.Json.JsonValueKind.Object);
    }

    [Fact]
    public async Task An_edit_that_drops_an_image_and_a_removal_end_its_use_and_only_an_article_has_a_cover()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var picture = IdOf(await Bring(ada, Jpeg(40)));
        var cover = IdOf(await Bring(ada, Jpeg(41)));
        var visitor = Agent(tangentSpace);

        var article = Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new StartTopic(Tids.Mint(), Articles, "Lanterns", "![Lit](/media/" + picture + ")", Ada, CoverId: cover), Cancellation)).Result;
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/media/" + picture)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/media/" + cover)).Status);

        // The same request sent again is the same article, and another cover under its Id is another request.
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(article.Id, Articles, "Lanterns", "![Lit](/media/" + picture + ")", Ada, CoverId: cover), Cancellation));
        Assert.IsType<Outcome<Post>.Conflict>(await writer.Send(new StartTopic(article.Id, Articles, "Lanterns", "![Lit](/media/" + picture + ")", Ada), Cancellation));

        var edited = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new EditPost(article.Id, article.Version, "Words alone.", null, Ada), Cancellation)).Result;
        Assert.Equal(cover, edited.CoverId);
        Assert.Empty(edited.MediaIds);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, "/media/" + picture)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(ada.Client, HttpMethod.Get, "/media/" + picture)).Status);

        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(article.Id, Ada), Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, "/media/" + cover)).Status);
        var tombstone = await Send(visitor, HttpMethod.Get, "/t/" + Articles + "/" + article.Slug, json: true);
        Assert.False(tombstone.Json.TryGetProperty("cover", out _));

        var discussion = await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Wicks", "Cotton.", Ada, CoverId: cover), Cancellation);
        Assert.Equal(Post.CoverOnArticle, Assert.IsType<Outcome<Post>.Refused>(discussion).Reason);
    }

    [Fact]
    public async Task The_plain_form_works_without_script_and_keeps_an_articles_cover_through_an_edit()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var cover = IdOf(await Bring(ada, Jpeg(40)));
        var article = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(Tids.Mint(), Articles, "Lanterns", "First words.", Ada, CoverId: cover), Cancellation)).Result;

        var path = TopicPath(article) + "/" + article.Id + "/edit";
        var page = await Send(ada.Client, HttpMethod.Get, path);
        Assert.Contains("name=\"" + PostWrites.BodyField + "\"", page.Body);
        Assert.Contains("name=\"" + PostWrites.TitleField + "\"", page.Body);
        Assert.Contains("name=\"" + PostWrites.CoverField + "\" value=\"" + cover + "\"", page.Body);

        var saved = await Form(ada, path, path, new()
        {
            [PostWrites.VersionField] = "1",
            [PostWrites.TitleField] = "Lanterns",
            [PostWrites.BodyField] = "Second words.",
            [PostWrites.CoverField] = cover
        });
        Assert.Equal(HttpStatusCode.SeeOther, saved.Status);
        var row = await Post.Get(article.Id, Cancellation);
        Assert.Equal(("Second words.", cover), (row!.Body, row.CoverId));
    }
}
