using System.Globalization;
using System.Text;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Lifecycle;

/// <summary>One Tangent or the whole Tangent Space, as its reader may view it, written to a <see cref="ZipSink"/> as it
/// is read: pages that open with no server, each beside its data. A page is the live page's resource through the live
/// page's component (<see cref="HtmlFormatter.Whole"/>), standing alone, and its data is that resource as its JSON
/// answers, so one file of each per bounded read: <c>index</c> and <c>page-{n}</c> at the root for the Tangents, under
/// <c>t/{tangent}/</c> for its Topics, and <c>index</c> and <c>{n}</c> under <c>t/{tangent}/{topic}/</c> for the windows of
/// a Topic, each <see cref="WindowSize"/> sequences wide, so a Post's file follows from its sequence and a link to it
/// needs nothing but the Post. Every link is relative, but one to what the zip does not hold: a mention's profile, a
/// Topic in a Tangent this zip leaves out, or a body's root-relative target, each of which names the place. The place's
/// stylesheet and fonts go in beside the pages; a Post's images stay the addresses its body names.
/// <para>A Tangent's Topics are walked by slug, which never changes, so one that gains a reply on the way is neither missed
/// nor written twice, and whatever started past the moment the export began is neither written nor listed. A removed Post
/// stays the tombstone a page shows, with no body in its page or its data, and nothing here reads a snapshot. Every read
/// is a bounded page and takes no lock, and what a page read is gone once it is written. A renderer keeps every component
/// it rendered until it is disposed, so each page has its own.</para></summary>
public sealed class Export
{
    /// <summary>The sequences one window of a Topic covers, so the most replies one file of it holds.</summary>
    public const int WindowSize = 50;

    /// <summary>How the place's files name a first page.</summary>
    private const string First = "index";

    private readonly ZipSink _zip;
    private readonly string? _personId;
    private readonly Tangents.Tangent? _only;
    private readonly TangentSpace _space;
    private readonly string _base;
    private readonly string _origin;
    private readonly string _accent;
    private readonly DateTimeOffset _asOf = DateTimeOffset.UtcNow;
    private readonly IServiceProvider _services;
    private readonly ResourceComponents _components;
    private readonly JsonSerializerSettings _json;
    private readonly ILoggerFactory _logs;

    private Export(ZipSink zip, string? personId, Tangents.Tangent? only, TangentSpace space, string baseAddress, IServiceProvider services)
    {
        _zip = zip;
        _personId = personId;
        _only = only;
        _space = space;
        _base = baseAddress;
        _origin = new Uri(baseAddress).GetLeftPart(UriPartial.Authority);
        _accent = Appearance.Word(space.Appearance.Accent);
        _services = services;
        _components = services.GetRequiredService<ResourceComponents>();
        _json = services.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value.SerializerSettings;
        _logs = services.GetRequiredService<ILoggerFactory>();
    }

    /// <summary>Writes the export onto <paramref name="output"/>: of <paramref name="only"/>, or of every Tangent the
    /// reader may view. A read that fails ends here with the zip unfinished, so a broken export never opens as a whole
    /// one.</summary>
    public static async Task Write(Stream output, string? personId, Tangents.Tangent? only, TangentSpace space, string baseAddress,
        IServiceProvider services, CancellationToken ct)
    {
        var zip = await ZipSink.Open(output, ct);
        var export = new Export(zip, personId, only, space, baseAddress, services);
        await export.Assets(ct);
        await export.Tangents(ct);
        await zip.Finish();
    }

    /// <summary>What a page's stylesheet needs, from where the place serves it.</summary>
    private async Task Assets(CancellationToken ct)
    {
        var root = _services.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        var stylesheet = root.GetFileInfo(HtmlFormatter.Stylesheet);
        if (stylesheet.Exists) await Copied(HtmlFormatter.Stylesheet, stylesheet, ct);
        foreach (var font in root.GetDirectoryContents(HtmlFormatter.Fonts).Where(file => !file.IsDirectory))
            await Copied(HtmlFormatter.Fonts + "/" + font.Name, font, ct);
    }

    private async Task Copied(string path, Microsoft.Extensions.FileProviders.IFileInfo file, CancellationToken ct)
    {
        await using var source = file.CreateReadStream();
        await _zip.Copy(path, source, ct);
    }

    /// <summary>The Tangents, a page of them at a time: what each holds, then the page that lists them.</summary>
    private async Task Tangents(CancellationToken ct)
    {
        if (_only is { } only)
        {
            await Holding(only, ct);
            await Listing([only], 1, more: false, ct);
            return;
        }

        ViewableTangents.Cursor? after = null;
        var page = 1;
        do
        {
            var read = await ViewableTangents.Read(_personId, ViewableTangents.Views, after, TangentsController.PageSize, ct);
            foreach (var tangent in read.Tangents) await Holding(tangent, ct);
            await Listing(read.Tangents, page++, read.Next is not null, ct);
            after = read.Next;
        } while (after is not null);
    }

    /// <summary>The root's page of the Tangents, each with where it opens.</summary>
    private Task Listing(IReadOnlyList<Tangents.Tangent> tangents, int page, bool more, CancellationToken ct) => Page(PagePath(page), new TangentsPage(
        tangents.Select(tangent => new TangentPage.Listed(tangent.Id, tangent.Name, PostWrites.Word(tangent.DefaultKind),
            "t/" + Uri.EscapeDataString(tangent.Id) + "/" + First + ".html", tangent.TopicCount)).ToList(),
        more ? PagePath(page + 1) + ".html" : null,
        page > 1,
        First + ".html",
        [],
        null,
        _space.Name,
        First + ".html",
        null,
        null), ".", null, ct);

    /// <summary>What a Tangent holds: every Topic, then the pages that list them as its page does.</summary>
    private async Task Holding(Tangents.Tangent tangent, CancellationToken ct)
    {
        var slug = "";
        while (true)
        {
            var roots = await BySlug(tangent.Id, slug, ct);
            foreach (var root in roots.Where(root => root.ParentId is null && root.CreatedAt <= _asOf)) await Topic(tangent, root, ct);
            if (roots.Count < TopicList.PageSize) break;
            slug = roots[^1].Slug!;
        }

        var audience = _space.Audience == Audience.Private || await Overwrites.AudienceOf(tangent.Id, ct) == Audience.Private
            ? Audience.Private
            : Audience.Public;
        TopicList.Cursor? cursor = null;
        var page = 1;
        do
        {
            var read = await TopicList.Read(tangent.Id, cursor, ct);
            var topics = read.Topics.Where(root => root.CreatedAt <= _asOf).ToList();
            await Page($"t/{tangent.Id}/{PagePath(page)}", await Listed(tangent, audience, topics, page++, read.Next is not null, ct), "../..",
                new Frame(_space.Name, "../../" + First + ".html", [new Crumb(_space.Name, "../../" + First + ".html"), new Crumb(tangent.Name)]), ct);
            cursor = read.Next;
        } while (cursor is not null);
    }

    /// <summary>A bounded page of a Tangent's Topics past the slug <paramref name="after"/>, one range of
    /// <c>ix_post_slugs</c>, which holds only roots past any slug at all.</summary>
    private static Task<IReadOnlyList<Post>> BySlug(string tangentId, string after, CancellationToken ct)
    {
        var query = new QueryDefinition
        {
            Filter = Filter.On(FieldPath.Of(nameof(Post.Slug)), FilterOperator.Gt, FilterValue.Of(after)),
            Page = 1,
            PageSize = TopicList.PageSize
        }.WithSort<Post>(sort => sort.OrderBy(post => post.Slug));
        return Post.Query(post => post.TangentId == tangentId, query, ct);
    }

    private async Task<TangentPage> Listed(Tangents.Tangent tangent, Audience audience, IReadOnlyList<Post> topics, int page, bool more, CancellationToken ct)
    {
        var latest = await TangentPageController.LatestReplies(topics, ct);
        var people = Standalone(await TangentPageController.People(topics, latest.Values, ct));
        var unseen = new Dictionary<string, long>();
        return new TangentPage(
            tangent.Id,
            tangent.Name,
            tangent.Description,
            PostWrites.Word(tangent.DefaultKind),
            audience.ToString().ToLowerInvariant(),
            First + ".html",
            [],
            topics.Select(root => TangentPageController.Topic(root, "", people, latest, unseen, signedIn: false)
                with { Address = Uri.EscapeDataString(root.Slug ?? root.Id) + "/" + First + ".html" }).ToList(),
            more ? PagePath(page + 1) + ".html" : null,
            page > 1,
            null,
            null,
            null,
            [new TangentPage.Listed(tangent.Id, tangent.Name, PostWrites.Word(tangent.DefaultKind), First + ".html", tangent.TopicCount)],
            _space.Name,
            "../../" + First + ".html",
            null,
            Undated);
    }

    /// <summary>A Topic, a window at a time.</summary>
    private async Task Topic(Tangents.Tangent tangent, Post root, CancellationToken ct)
    {
        var folder = $"t/{tangent.Id}/{root.Slug ?? root.Id}";
        var takenFrom = await Origin(root, ct);
        var windows = Math.Max(1, (root.LastSequence + WindowSize - 1) / WindowSize);
        for (long window = 1; window <= windows; window++)
        {
            var replies = await TopicWindow.Between(root.Id, (window - 1) * WindowSize, window * WindowSize, ct);
            var path = window == 1 ? First : window.ToString(CultureInfo.InvariantCulture);
            await Page(folder + "/" + path, await Window(tangent, root, replies, window, windows, takenFrom, ct), "../../..", null, ct);
        }
    }

    /// <summary>One window as its page shows it to no one in particular: nothing it offers, and nothing new.</summary>
    private async Task<TopicPage> Window(Tangents.Tangent tangent, Post root, IReadOnlyList<Post> replies, long window, long windows,
        TopicPage.Origin? takenFrom, CancellationToken ct)
    {
        var posts = replies.Prepend(root).ToList();
        var people = Standalone(await TopicPageController.People(posts, _base, ct, posts.Count * 3));
        var mentioned = await Mentions.Resolve(posts, _base, ct);
        var taken = await Origins.From(posts, _personId, _base, ct);
        var sequences = await Sequences(root, replies, ct);
        return new TopicPage(
            root.Id,
            root.Slug ?? root.Id,
            root.Title ?? "",
            PostWrites.Word(root.Kind),
            First + ".html",
            new TopicPage.InTangent(tangent.Id, tangent.Name, "../" + First + ".html"),
            root.Locked,
            root.Pinned,
            root.ReplyCount,
            [],
            null,
            null,
            null,
            null,
            Shown(root, root, people, mentioned, taken, sequences),
            replies.Select(reply => Shown(reply, root, people, mentioned, taken, sequences)).ToList(),
            replies.Count == 0 ? null : replies[0].Sequence,
            replies.Count == 0 ? null : replies[^1].Sequence,
            window > 1 ? WindowFile(window - 1) : null,
            window < windows ? WindowFile(window + 1) : null,
            null,
            null,
            _space.Name,
            "../../../" + First + ".html",
            null,
            null,
            Undated,
            takenFrom);
    }

    private TopicPage.Post Shown(Post post, Post root, IReadOnlyDictionary<string, TangentPage.Author> people,
        IReadOnlyDictionary<string, TopicPage.Mention> mentioned, IReadOnlyDictionary<string, TopicPage.Taken> taken,
        IReadOnlyDictionary<string, long> sequences)
    {
        var parent = post.ParentId is { } parentId && parentId != root.Id ? parentId : null;
        return new TopicPage.Post(
            post.Id,
            post.Sequence,
            FileOf(post.Sequence) + "#" + post.Id,
            TopicPageController.Person(people, post.AuthorId),
            post.Removed ? null : post.Body,
            post.Removed ? [] : Mentions.Of(post, mentioned),
            post.CreatedAt,
            post.Version,
            parent,
            parent is not null && sequences.TryGetValue(parent, out var sequence) ? FileOf(sequence) + "#" + parent : null,
            post.Removed ? new TopicPage.Removal(TopicPageController.Person(people, post.RemovedBy ?? post.AuthorId), post.RemovedAt ?? post.CreatedAt) : null,
            false,
            [],
            null,
            null,
            taken.GetValueOrDefault(post.Id) is { } topics
                ? topics with { Topics = topics.Topics.Select(topic => topic with { Address = Local(topic.Address, topic.Address, First + ".html") }).ToList() }
                : null,
            null,
            TangentPage.EditedOf(post, people));
    }

    /// <summary>The sequences of the replies these answer, by Id: those the window holds, and the others in one keyed read,
    /// which say where their files are.</summary>
    private static async Task<IReadOnlyDictionary<string, long>> Sequences(Post root, IReadOnlyList<Post> replies, CancellationToken ct)
    {
        var known = replies.ToDictionary(reply => reply.Id, reply => reply.Sequence, StringComparer.Ordinal);
        var others = replies.Select(reply => reply.ParentId).OfType<string>()
            .Where(id => id != root.Id && !known.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
        if (others.Count > 0)
            foreach (var parent in (await Post.Get(others, ct)).OfType<Post>())
                known[parent.Id] = parent.Sequence;
        return known;
    }

    /// <summary>Where a Topic was taken on a tangent from, as the reader may know it, its address relative where the zip
    /// holds it.</summary>
    private async Task<TopicPage.Origin?> Origin(Post root, CancellationToken ct)
    {
        var origin = await Origins.Of(root, _personId, _base, ct);
        if (origin is not { Absent: false, Address: { } address } || await Post.Get(root.OriginId!, ct) is not { } post) return origin;
        return origin with { Address = Local(address, address[..address.LastIndexOf('/')], FileOf(post.Sequence) + "#" + post.Id) };
    }

    /// <summary>Where another Topic's page is, seen from a Topic's folder: <paramref name="file"/> of the Topic at the live
    /// address <paramref name="topic"/> where the zip holds its Tangent, and otherwise <paramref name="live"/>, the live
    /// address of what is linked.</summary>
    private string Local(string live, string topic, string file)
    {
        var inPlace = topic[(_base.Length + "/t/".Length)..];
        return _only is { } only && inPlace[..inPlace.IndexOf('/')] != Uri.EscapeDataString(only.Id) ? live : "../../" + inPlace + "/" + file;
    }

    /// <summary>The window's file of a Post by its sequence, the root's in the first.</summary>
    private static string FileOf(long sequence) => WindowFile(sequence <= 0 ? 1 : (sequence - 1) / WindowSize + 1);

    private static string WindowFile(long window) => (window == 1 ? First : window.ToString(CultureInfo.InvariantCulture)) + ".html";

    private static string PagePath(int page) => page == 1 ? First : "page-" + page.ToString(CultureInfo.InvariantCulture);

    /// <summary>The day a page's times are written for: none, so each is a full date, which a page kept for years goes on
    /// meaning.</summary>
    private static readonly DateOnly Undated = DateOnly.MinValue;

    /// <summary>The people as a page standing alone shows them: by their marks, since their avatars and their profiles are
    /// elsewhere.</summary>
    private static Dictionary<string, TangentPage.Author> Standalone(IReadOnlyDictionary<string, TangentPage.Author> people)
        => people.ToDictionary(person => person.Key, person => person.Value with { Avatar = null, Profile = null }, StringComparer.Ordinal);

    /// <summary>One resource at <paramref name="path"/>: its page, <paramref name="up"/> being the way from there to the
    /// zip's root, and its data beside it.</summary>
    private async Task Page<TResource>(string path, TResource model, string up, Frame? frame, CancellationToken ct) where TResource : notnull
    {
        var resource = _components.For(typeof(TResource));
        string html;
        await using (var renderer = new HtmlRenderer(_services, _logs))
            html = await HtmlFormatter.Whole(renderer, resource, model, up, _accent, frame ?? resource.Frame?.Invoke(model), standalone: true);
        await _zip.Write(path + ".html", Encoding.UTF8.GetBytes(Feed.Rooted(html, _origin)), ct);
        await _zip.Write(path + ".json", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(model, _json)), ct);
    }
}
