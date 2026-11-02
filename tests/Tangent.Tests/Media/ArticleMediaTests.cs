using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Space;
using Tangent.Tangents;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>An article's images: brought in the page or by an agent, kept re-encoded and without their metadata, used as
/// a cover or in a body, and shown only to whoever may read a Post that uses them.</summary>
public sealed class ArticleMediaTests
{
    private const string Secrets = "SecretCamera|Secret Artist|Secret-Byline|Secret-City|Secret-Xmp|Secret-Png-Text";

    /// <summary>What marks metadata in an encoded file, whatever it says: an Exif, XMP or Photoshop (IPTC) segment, or a PNG's
    /// text chunk.</summary>
    private const string MetadataMarks = "Exif\0\0|http://ns\\.adobe\\.com/xap|Photoshop 3\\.0|tEXt|iTXt|zTXt";

    private static SKBitmap Filled(int size, SKColor colour)
    {
        var image = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Opaque));
        image.Erase(colour);
        return image;
    }

    /// <summary>A JPEG of a side of <paramref name="size"/>, with an Exif, an XMP and an IPTC segment planted after its start
    /// where <paramref name="secrets"/> asks, as a camera or an editor writes them.</summary>
    internal static byte[] Jpeg(int size = 64, bool secrets = true)
    {
        using var image = Filled(size, new SKColor(0, 128, 128));
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var jpeg = encoded.ToArray();
        if (!secrets) return jpeg;
        byte[] planted =
        [
            .. Segment(0xE1, [.. "Exif\0\0"u8, .. Tiff((0x010F, "SecretCamera"), (0x013B, "Secret Artist"))]),
            .. Segment(0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><a>Secret-Xmp</a></x:xmpmeta>"u8]),
            .. Segment(0xED, [.. "Photoshop 3.0\08BIM"u8, 0x04, 0x04, 0, 0, .. Iptc((80, "Secret-Byline"), (90, "Secret-City"))])
        ];
        return [.. jpeg[..2], .. planted, .. jpeg[2..]];
    }

    private static byte[] Segment(byte marker, byte[] content)
        => [0xFF, marker, (byte)((content.Length + 2) >> 8), (byte)(content.Length + 2), .. content];

    /// <summary>A little-endian TIFF of ASCII entries in tag order, its strings after its one directory.</summary>
    private static byte[] Tiff(params (ushort Tag, string Text)[] entries)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        writer.Write("II*\0"u8);
        writer.Write(8);
        writer.Write((ushort)entries.Length);
        var at = 8 + 2 + entries.Length * 12 + 4;
        foreach (var (tag, text) in entries)
        {
            writer.Write(tag);
            writer.Write((ushort)2);
            writer.Write(text.Length + 1);
            writer.Write(at);
            at += text.Length + 1;
        }

        writer.Write(0);
        foreach (var (_, text) in entries)
        {
            writer.Write(Encoding.ASCII.GetBytes(text));
            writer.Write((byte)0);
        }

        return bytes.ToArray();
    }

    /// <summary>A Photoshop resource holding IPTC datasets of record 2.</summary>
    private static byte[] Iptc(params (byte Dataset, string Text)[] sets)
    {
        byte[] data = [.. sets.SelectMany(set => (byte[])[0x1C, 2, set.Dataset, 0, (byte)set.Text.Length, .. Encoding.ASCII.GetBytes(set.Text)])];
        byte[] length = [0, 0, (byte)(data.Length >> 8), (byte)data.Length];
        return [.. length, .. data, .. data.Length % 2 == 1 ? new byte[] { 0 } : []];
    }

    /// <summary>A PNG with a text chunk planted after its header.</summary>
    private static byte[] PngWithText()
    {
        using var image = Filled(32, new SKColor(255, 127, 80));
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var png = encoded.ToArray();
        byte[] named = [.. "tEXtComment\0Secret-Png-Text"u8];
        byte[] length = [0, 0, 0, (byte)(named.Length - 4)];
        var crc = Crc(named);
        byte[] chunk = [.. length, .. named, (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc];
        const int afterHeader = 8 + 25;
        return [.. png[..afterHeader], .. chunk, .. png[afterHeader..]];
    }

    private static uint Crc(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }

    private static int WidthOf(byte[] image)
    {
        using var codec = SKCodec.Create(new MemoryStream(image));
        return codec.Info.Width;
    }

    /// <summary>Brings an image in as the page does: a multipart form with the page's token, answered as JSON.</summary>
    internal static async Task<Answered> Bring(Browser browser, byte[] image, string tangent = Articles, string page = "/t/" + Articles + "/new")
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

    internal static string IdOf(Answered brought) => brought.Json.GetProperty("id").GetString()!;

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
        Assert.Equal(64, WidthOf(card));
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
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, "did:plc:leo4writes", [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, "did:plc:leo4writes");
        using var bea = await SignedIn(tangentSpace, Bea);

        var brought = await Bring(leo, Jpeg(), Studio, "/t/" + Studio + "/new");
        var id = IdOf(brought);
        var reference = "/media/" + id;
        var post = await Started("did:plc:leo4writes", "Plans", "![The plan](" + reference + ")", Studio);
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
        var naming = await Started(Bea, "Look", "![Mine now](" + reference + ")");
        Assert.False(await MediaUse.Reaches(Bea, id, Cancellation));
        Assert.Null(await MediaUse.Get(MediaUse.Key(id, naming.Id), Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(bea.Client, HttpMethod.Get, reference)).Status);
        var stolen = await new StartTopic(Tids.Mint(), Articles, "Mine", "Words.", Bea, CoverId: id).Send(Cancellation);
        Assert.Equal(Post.NoCover, Assert.IsType<Outcome<Post>.Refused>(stolen).Reason);
    }

    [Fact]
    public async Task In_a_private_place_an_image_is_absent_to_a_visitor()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var id = IdOf(await Bring(ada, Jpeg()));
        await Started(Ada, "Open", "![Open](/media/" + id + ")", Articles);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        space!.Audience = Audience.Private;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));

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
            Assert.Matches(MetadataMarks, Encoding.Latin1.GetString(sent));
            using (var opened = SKBitmap.Decode(sent)) Assert.NotNull(opened);
            var brought = await Bring(ada, sent);
            Assert.Equal(HttpStatusCode.Created, brought.Status);
            var kept = await Read(ada.Client, "/media/" + IdOf(brought));
            Assert.DoesNotMatch(Secrets, Encoding.Latin1.GetString(kept));
            Assert.DoesNotMatch(MetadataMarks, Encoding.Latin1.GetString(kept));
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
        Assert.Equal(Media.LongestEdge, WidthOf(await Read(ada.Client, "/media/" + IdOf(large))));

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
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word), (visitor.Status, visitor.Gate));
        var hidden = await Send(bea.Client, HttpMethod.Post, "/t/" + Studio + "/media", JsonBody(new { file = Convert.ToBase64String(Jpeg()) }), json: true, token: token);
        Assert.Equal((HttpStatusCode.NotFound, Capabilities.Absent.Text), (hidden.Status, hidden.Gate));
        var discussion = await Send(bea.Client, HttpMethod.Get, "/t/" + Workshop + "/new", json: true);
        Assert.False(discussion.Json.TryGetProperty("bring", out var bring) && bring.ValueKind == System.Text.Json.JsonValueKind.Object);
    }

    [Fact]
    public async Task An_edit_that_drops_an_image_and_a_removal_end_its_use_and_only_an_article_has_a_cover()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var picture = IdOf(await Bring(ada, Jpeg(40)));
        var cover = IdOf(await Bring(ada, Jpeg(41)));
        var visitor = Agent(tangentSpace);

        var article = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Articles, "Lanterns", "![Lit](/media/" + picture + ")", Ada, CoverId: cover).Send(Cancellation)).Result;
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/media/" + picture)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/media/" + cover)).Status);

        // The same request sent again is the same article, and another cover under its Id is another request.
        Assert.IsType<Outcome<Post>.Done>(await new StartTopic(article.Id, Articles, "Lanterns", "![Lit](/media/" + picture + ")", Ada, CoverId: cover).Send(Cancellation));
        Assert.Equal(ReasonKind.Conflict, Assert.IsType<Outcome<Post>.Refused>(await new StartTopic(article.Id, Articles, "Lanterns", "![Lit](/media/" + picture + ")", Ada).Send(Cancellation)).Reason.Kind);

        var edited = Assert.IsType<Outcome<Post>.Done>(await new EditPost(article.Id, article.Version, "Words alone.", null, Ada).Send(Cancellation)).Result;
        Assert.Equal(cover, edited.CoverId);
        Assert.Empty(edited.MediaIds);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, "/media/" + picture)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(ada.Client, HttpMethod.Get, "/media/" + picture)).Status);

        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(article.Id, Ada).Send(Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, "/media/" + cover)).Status);
        var tombstone = await Send(visitor, HttpMethod.Get, "/t/" + Articles + "/" + article.Slug, json: true);
        Assert.False(tombstone.Json.TryGetProperty("cover", out _));

        var discussion = await new StartTopic(Tids.Mint(), Workshop, "Wicks", "Cotton.", Ada, CoverId: cover).Send(Cancellation);
        Assert.Equal(Post.CoverOnArticle, Assert.IsType<Outcome<Post>.Refused>(discussion).Reason);
    }

    [Fact]
    public async Task An_articles_header_stands_once_in_its_head_between_its_cover_and_its_title()
    {
        const string header = "<p>From the long reads.</p>";
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var cover = IdOf(await Bring(ada, Jpeg()));
        var article = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Articles, "Lanterns", "Words.", Ada, CoverId: cover).Send(Cancellation)).Result;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        space!.Slots.ArticleHeader = header;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));

        var page = (await Send(Agent(tangentSpace), HttpMethod.Get, TopicPath(article))).Body;

        var head = page.IndexOf("<header class=\"article-head\">", StringComparison.Ordinal);
        var figure = page.IndexOf("class=\"article-figure\"", StringComparison.Ordinal);
        var slot = page.IndexOf("slot-article-header", StringComparison.Ordinal);
        var title = page.IndexOf("<h1 class=\"article-title\"", StringComparison.Ordinal);
        Assert.True(head >= 0 && head < figure && figure < slot && slot < title, "The header is in the article's head, under its cover and over its title.");
        Assert.Equal(slot, page.LastIndexOf("slot-article-header", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_public_articles_card_previews_its_cover_large_and_a_card_with_no_image_stays_small()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Overwrites.SetAudience(tangentSpace.Services.GetRequiredService<RoleCollection>(), Articles, Audience.Public, Cancellation);
        await Given.AParticipant(tangentSpace, "did:plc:leo4writes", [Roles.Administrator], ct: Cancellation);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var leo = await SignedIn(tangentSpace, "did:plc:leo4writes");
        var cover = IdOf(await Bring(ada, Jpeg()));
        var article = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Articles, "Lanterns", "Words.", Ada, CoverId: cover).Send(Cancellation)).Result;
        var bare = await Started(Ada, "No cover here", "Words.", Articles);

        var page = (await Send(Agent(tangentSpace), HttpMethod.Get, TopicPath(article))).Body;
        Assert.Contains($"<meta property=\"og:image\" content=\"{BaseUrl}/media/{cover}/{MediaRecipes.CardName}\"", page);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\"", page);
        Assert.Equal(HttpStatusCode.OK, (await Send(Agent(tangentSpace), HttpMethod.Get, $"/media/{cover}/{MediaRecipes.CardName}")).Status);

        var plain = (await Send(Agent(tangentSpace), HttpMethod.Get, TopicPath(bare))).Body;
        Assert.DoesNotContain("og:image", plain);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary\"", plain);

        // Where a visitor may not read, there is no card and so no cover in it, even for who may read there.
        var hidden = IdOf(await Bring(leo, Jpeg(50), Studio, "/t/" + Studio + "/new"));
        var inside = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Studio, "Inside", "Words.", "did:plc:leo4writes").Send(Cancellation)).Result;
        var kept = (await Send(leo.Client, HttpMethod.Get, TopicPath(inside))).Body;
        Assert.DoesNotContain("og:image", kept);
        Assert.DoesNotContain("twitter:", kept);
        Assert.DoesNotContain(hidden, kept);
    }

    [Fact]
    public async Task A_feed_reader_with_no_account_fetches_an_articles_images_under_the_places_path()
    {
        const string place = "https://tangent.example/notes";
        var settings = Settings();
        settings["Koan:Web:ApplicationUrl"] = place;
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(settings, ct: Cancellation);
        await Place(tangentSpace);
        var cover = await Media.Keep(Jpeg(), Cancellation);
        var inline = await Media.Keep(Jpeg(40), Cancellation);
        foreach (var image in new[] { cover, inline }) Assert.IsType<Outcome<MediaUse>.Done>(await new BringMedia(image.Id, Ada).Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Articles, "Lanterns", "![Lit](/media/" + inline.Id + ")", Ada, CoverId: cover.Id).Send(Cancellation));
        using var reader = Agent(tangentSpace);

        foreach (var feed in new[] { "/notes" + Feeds.ArticlesPath, "/notes/t/" + Articles + "/" + Feeds.TangentSegment })
        {
            var json = (await Send(reader, HttpMethod.Get, feed + Render.JsonFeed.Extension)).Body;
            var item = System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("items")[0];
            Assert.Equal($"{place}/media/{cover.Id}", item.GetProperty("image").GetString());
            Assert.Contains($"src=\"{place}/media/{inline.Id}\"", item.GetProperty("content_html").GetString());

            var atom = (await Send(reader, HttpMethod.Get, feed + Render.AtomFeed.Extension)).Body;
            Assert.Contains($"<media:thumbnail url=\"{place}/media/{cover.Id}\"", atom);
            Assert.Contains($"src=\"{place}/media/{inline.Id}\"", atom);
        }

        foreach (var image in new[] { cover, inline })
            Assert.Equal(HttpStatusCode.OK, (await Send(reader, HttpMethod.Get, "/notes/media/" + image.Id)).Status);

        // An entry with no cover says nothing of one.
        Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Articles, "Words alone", "Words.", Ada).Send(Cancellation));
        var newest = System.Text.Json.JsonDocument.Parse((await Send(reader, HttpMethod.Get, "/notes" + Feeds.ArticlesPath + Render.JsonFeed.Extension)).Body).RootElement.GetProperty("items")[0];
        Assert.False(newest.TryGetProperty("image", out _));
    }

    [Fact]
    public async Task What_a_body_and_a_slot_keep_of_an_image_here_is_one_rule_its_address_satisfies()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var reference = Media.Reference(IdOf(await Bring(ada, Jpeg())));
        Assert.Equal(reference, Media.Reference(Media.IdIn(reference)!));

        Assert.Contains($"<img src=\"{reference}\"", MarkdownHtml.Render("![A wick](" + reference + ")").Html());
        Assert.Contains($"<img src=\"{reference}\"", new Slots().Written(null, null, null, $"<img src=\"{reference}\" alt=\"\">", null).ArticleHeader);
    }

    private const string Keeper = "did:plc:leo4writes";

    private static string Img(string id) => $"<img src=\"/media/{id}\" alt=\"\">";

    private static async Task Slots(string? masthead = null, string? doorIntro = null, string? footer = null, string? head = null, Audience? audience = null)
        => Assert.IsType<Outcome<TangentSpace>.Done>(await new ChangeTangentSpace(Keeper, Masthead: masthead, DoorIntro: doorIntro, Footer: footer, Head: head, Audience: audience).Send(Cancellation));

    [Fact]
    public async Task An_image_a_slot_names_reaches_whoever_reads_its_page_and_ends_when_no_slot_names_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Keeper, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Keeper);
        using var visitor = Agent(tangentSpace);
        var logo = IdOf(await Bring(leo, Jpeg(64)));
        var icon = IdOf(await Bring(leo, Jpeg(32)));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, "/media/" + logo)).Status);

        await Slots(masthead: Img(logo), head: $"<link rel=\"icon\" href=\"/media/{icon}\">");
        Assert.Contains($"src=\"{BaseUrl}/media/{logo}\"", (await Send(visitor, HttpMethod.Get, "/")).Body);
        foreach (var path in new[] { "/media/" + logo, $"/media/{logo}/{MediaRecipes.CardName}", "/media/" + icon })
            Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, path)).Status);

        // Moved to another slot it stays, even for someone who reaches it only through the slot; named in no slot it ends.
        await Slots(masthead: "", footer: Img(logo));
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/media/" + logo)).Status);
        Assert.Null(await MediaUse.Get(MediaUse.Key(logo, "slot:" + Space.Slots.MastheadName), Cancellation));
        await Slots(footer: "");
        Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, "/media/" + logo)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(leo.Client, HttpMethod.Get, "/media/" + logo)).Status);

        // An image used elsewhere goes on being used; a slot is no way to reach an image its writer may not see.
        var hidden = IdOf(await Bring(leo, Jpeg(50), Studio, "/t/" + Studio + "/new"));
        await Started(Keeper, "Plans", "![The plan](/media/" + hidden + ")", Studio);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, "/media/" + hidden)).Status);
        var keeperRole = "role:keeper";
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await roles.Define(keeperRole, "Keeper", Bits.Tokens(Bit.ManageSpace), ct: Cancellation);
        await Given.AParticipant(tangentSpace, "did:plc:kay4writes", [Roles.Member, keeperRole], ct: Cancellation);
        Assert.IsType<Outcome<TangentSpace>.Done>(await new ChangeTangentSpace("did:plc:kay4writes", Masthead: Img(hidden)).Send(Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, "/media/" + hidden)).Status);
    }

    [Fact]
    public async Task In_a_private_place_an_image_on_every_page_reaches_a_visitor_and_one_on_the_door_does_not()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Keeper, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Keeper);
        using var bea = await SignedIn(tangentSpace, Bea);
        using var visitor = Agent(tangentSpace);
        var logo = IdOf(await Bring(leo, Jpeg(64)));
        var welcome = IdOf(await Bring(leo, Jpeg(65)));
        await Slots(masthead: Img(logo), doorIntro: Img(welcome), audience: Audience.Private);

        // The sign-in, all a visitor reads here, carries the masthead, so its logo shows; the door's picture stays inside.
        Assert.Contains($"src=\"{BaseUrl}/media/{logo}\"", (await Send(visitor, HttpMethod.Get, "/")).Body);
        Assert.DoesNotContain(welcome, (await Send(visitor, HttpMethod.Get, "/")).Body);
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/media/" + logo)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, "/media/" + welcome)).Status);
        Assert.Contains($"src=\"{BaseUrl}/media/{welcome}\"", (await Send(bea.Client, HttpMethod.Get, "/")).Body);
        Assert.Equal(HttpStatusCode.OK, (await Send(bea.Client, HttpMethod.Get, "/media/" + welcome)).Status);
    }

    [Fact]
    public async Task Under_a_path_an_image_a_slot_names_is_at_its_address()
    {
        const string place = "https://tangent.example/notes";
        var settings = Settings();
        settings["Koan:Web:ApplicationUrl"] = place;
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(settings, ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Keeper, [Roles.Administrator], ct: Cancellation);
        var logo = await Media.Keep(Jpeg(), Cancellation);
        Assert.IsType<Outcome<MediaUse>.Done>(await new BringMedia(logo.Id, Keeper).Send(Cancellation));
        await Slots(masthead: Img(logo.Id));
        using var visitor = Agent(tangentSpace);

        Assert.Contains($"src=\"{place}/media/{logo.Id}\"", (await Send(visitor, HttpMethod.Get, "/notes/")).Body);
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/notes/media/" + logo.Id)).Status);
    }

    [Fact]
    public async Task Only_the_render_the_place_names_answers_and_a_format_or_a_size_in_the_address_does_not()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var id = IdOf(await Bring(ada, Jpeg()));
        await Started(Ada, "Open", "![Open](/media/" + id + ")", Articles);
        using var visitor = Agent(tangentSpace);

        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, $"/media/{id}/{MediaRecipes.CardName}")).Status);
        foreach (var path in new[] { "png", "webp", "jpeg", "png?w=33" })
            Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, $"/media/{id}/{path}")).Status);
    }

    [Fact]
    public async Task Llms_txt_says_how_a_picture_comes_into_an_article()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);

        var text = await Agent(tangentSpace).GetStringAsync("/llms.txt", Cancellation);

        foreach (var word in new[] { "`bring`", "`reference`", "`cover`", "![what it shows](reference)" }) Assert.Contains(word, text);
    }

    [Fact]
    public async Task The_plain_form_works_without_script_and_keeps_an_articles_cover_through_an_edit()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var cover = IdOf(await Bring(ada, Jpeg(40)));
        var article = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Articles, "Lanterns", "First words.", Ada, CoverId: cover).Send(Cancellation)).Result;

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
