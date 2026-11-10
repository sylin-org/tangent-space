using System.ComponentModel;
using Koan.Data.Abstractions;
using Koan.Identity.Roles;
using Koan.Web.Attributes;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Koan.Web.Hosting;
using KoanPerson = Koan.Identity.Identity;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Participants;

/// <summary>What reading a profile takes beside its address: where the page of Posts continues.</summary>
public sealed record ProfileInput([property: Description(Holds.PostsBefore)] string? Before = null);

/// <summary>A participant's profile, one resource both ways, at <c>/p/{handle}</c>: a declared read of Koan's over
/// <see cref="Participant"/>, which serves that read alone. The address takes a handle, matched as the participant's row
/// keeps it, a participant's id, as a mention links to them, or the DID of the account they are bound to (<see cref="KeyOf"/>);
/// a reader without <see cref="Bit.View"/> on the place receives its gate, the same for every handle, held or not, and a
/// handle no participant holds now answers as a missing resource. Its emit hook composes the page from the row
/// (<see cref="Compose"/>): the profile their PDS gave, their kind, the roles they hold here, when they arrived, and their
/// recent Posts in the Tangents the reader may view, left to the formatter as HTML, a fragment or JSON. Every read is
/// bounded and keyed: capability's rows for the place; the participant by handle, id or DID; Koan's roles of theirs and
/// their person's row; the page of their Posts in the Tangents the reader may view, continued from <c>before</c>, with the
/// Tangents those Posts name and what the reader holds on each, read again past a Post the reader may not view, at most
/// <see cref="ViewableTangents.Met.ReadLimit"/> reads (<see cref="RecentPosts"/>); the roots of the replies among them;
/// and the participants those Posts mention. For a reader who holds <see cref="Bit.ManageStanding"/>
/// or <see cref="Bit.ManageRoles"/>, it also reads the participant as the place's people list them, with what the reader
/// may change of them and the forms that do, which post to <see cref="PeopleController"/> and come back here.</summary>
[Route("/p")]
[Declared]
public sealed class ProfilesController(KoanWebBaseUrl baseUrl, RoleCollection roles) : EntityController<Participant>, IEmitHook<Participant>
{
    /// <summary>The query parameter that carries the cursor of the page of Posts to continue from.</summary>
    public const string Before = "before";

    private RecentPosts.Cursor? _before;

    [HttpGet("{handle}")]
    [Tool(Name = "read_profile")]
    [Description("Read a participant's profile, by handle or DID: who they are, their kind, their roles here and their recent Posts in the Tangents you may read. Call this to know who you are talking with.")]
    [Needs(Bit.View)]
    [Input<ProfileInput>]
    public override async Task<IActionResult> GetById([Description(Holds.Handle)] string handle, CancellationToken ct)
    {
        if (Request.Query.TryGetValue(Before, out var before) && (_before = RecentPosts.Cursor.Parse(before.ToString())) is null)
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {Before} parameter is the cursor a page of this profile names as its older.") { Title = "Not a page of these posts" }.ToResult();
        var answer = await base.GetById(handle, ct);
        return answer is NotFoundResult ? this.Refused(Capabilities.Absent) : answer;
    }

    /// <summary>The id of the participant the address names: the address itself where it is a participant's id, as a mention
    /// links to them; by the account they are bound to now where it is a DID; otherwise the one participant whose row keeps
    /// the handle, as written or in lower case, as atproto writes handles. A handle has one holder: the command records a
    /// verified handle and takes it from any other row in one command (<see cref="Handles.Release"/>), so one keyed read of
    /// one row answers, and the row is then read by its id.</summary>
    protected override async ValueTask<string?> KeyOf(string segment, CancellationToken ct)
    {
        if (segment.Length is 0 or > Mentions.IdentityLimit) return null;
        if (Participant.IsId(segment)) return segment;
        var query = new QueryDefinition { Page = 1, PageSize = 1 };
        if (Participant.IsDid(segment)) return (await Participant.Query(participant => participant.Did == segment, query, ct)).FirstOrDefault()?.Id;
        if (segment.Length > Participants.SignIn.HandleLimit) return null;
        var rows = await Participant.Query(participant => participant.Handle == segment, query, ct);
        var lower = segment.ToLowerInvariant();
        if (rows.Count == 0 && lower != segment) rows = await Participant.Query(participant => participant.Handle == lower, query, ct);
        return rows.FirstOrDefault()?.Id;
    }

    /// <summary>The profile the row read makes.</summary>
    public async Task<EmitDecision> OnEmitModel(HookContext<Participant> ctx, object payload)
        => payload is Participant subject ? EmitDecision.With(await Compose(ctx.Http!, baseUrl, roles, subject, _before, ctx.Ct)) : EmitDecision.Next();

    /// <summary>The profile of <paramref name="subject"/> as the reader the Doorway found on <paramref name="http"/> reads it,
    /// its Posts continued from <paramref name="cursor"/>: what the read answers, and what the archive keeps for a visitor.</summary>
    public static async Task<ProfilePage> Compose(HttpContext http, KoanWebBaseUrl baseUrl, RoleCollection roles, Participant subject,
        RecentPosts.Cursor? cursor, CancellationToken ct)
    {
        var personId = PostWrites.PersonId(http.User);
        var rows = Here.Of(http).Rows;
        var place = rows.For(null);
        var tangents = new ViewableTangents.Met(personId);
        var page = await RecentPosts.Read(subject.Id, tangents, cursor, ct);
        var roots = await Roots(page.Posts, ct);
        var people = await Named(page.Posts, ct);
        var held = (await roles.ForPerson(subject.Id, ct)).Where(role => role.Key != Roles.Everyone).Select(role => role.Name)
            .Order(StringComparer.Ordinal).ToList();
        var person = await KoanPerson.Get(subject.Id, ct);

        var baseAddress = baseUrl.Resolve(http);
        var (name, shownHandle) = (subject.ShownName(), subject.ShownHandle());
        var address = ProfilePage.AddressOf(baseAddress, shownHandle);
        var reader = rows.Participant;
        // The reader's own profile, and one carrying forms that change someone, is never stored, so a browser's Back after
        // sign-out redisplays nothing of it; anyone else's, read signed in, is theirs alone to cache.
        if (reader is not null) http.Response.Headers.CacheControl = reader.Id == subject.Id ? "no-cache, no-store" : "private";

        // Whoever may change the participant's standing or roles reads them as the people list them, with the forms that
        // change them, coming back here.
        PeoplePage.Listed? listed = null;
        PeopleForms? forms = null;
        if (reader is not null && PeopleController.ReadGate(place) is null)
        {
            listed = await (await PeopleController.Lister.Load(roles, place, reader.Id, baseAddress, ct)).Row(subject, ct);
            forms = PeopleController.Forms(place, baseAddress, baseUrl.PathBase, ProfilePage.Segment + Uri.EscapeDataString(shownHandle));
            http.Response.Headers.CacheControl = "no-cache, no-store";
        }

        // Their own profile offers how the place shows them, and leaving it, where they may leave.
        var own = reader is not null && reader.Id == subject.Id;
        var back = Uri.EscapeDataString(baseUrl.PathBase + ProfilePage.Segment + shownHandle);
        return new ProfilePage(
            subject.Id,
            subject.Did,
            shownHandle,
            name,
            subject.Profile.DisplayName is { Length: > 0 } displayName ? displayName : null,
            subject.Profile.Description is { Length: > 0 } description ? description : null,
            Monogram.Avatar(subject.Profile.Avatar),
            subject.Kind.ToString().ToLowerInvariant(),
            held,
            person?.CreatedAt,
            address,
            place.Can,
            place.Gate(Bit.Post)?.Word,
            page.Posts.Select(post => Written(post, roots, tangents, people, baseAddress)).ToList(),
            page.Next is { } next ? address + "?" + Before + "=" + Uri.EscapeDataString(next.ToString()) : null,
            cursor is not null,
            rows.Space.Name,
            baseAddress + "/",
            reader is null ? baseAddress + SignInController.Path + "?return=" + back : null,
            reader is null ? null : Reader(reader, place, baseAddress, back, await Events.Unread.BadgeFor(reader.Id, baseAddress, ct)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            listed,
            forms,
            own ? ProfilePage.Kinds(baseAddress, subject) : null,
            own && LeavePlace.Refusal(subject, rows.Space, DateTimeOffset.UtcNow) is null ? ProfilePage.Leaving(baseAddress) : null);
    }

    /// <summary>The roots of the replies among the Posts, by Id, in one keyed read.</summary>
    private static async Task<IReadOnlyDictionary<string, Post>> Roots(IReadOnlyList<Post> posts, CancellationToken ct)
    {
        var ids = posts.Where(post => post.ParentId is not null).Select(post => post.TopicId).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Post>();
        return (await Post.Get(ids, ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);
    }

    /// <summary>The participants the Posts mention and who else edited them, as their openings read them, in one keyed read.</summary>
    private static async Task<IReadOnlyDictionary<string, Render.Person>> Named(IReadOnlyList<Post> posts, CancellationToken ct)
    {
        var ids = posts.SelectMany(post => post.MentionedIds).Concat(posts.Select(post => post.EditedBy).OfType<string>()).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Render.Person>();
        return (await Participant.Get(ids, ct)).OfType<Participant>().ToDictionary(row => row.Id, row => Render.Person.Of(row), StringComparer.Ordinal);
    }

    private static ProfilePage.Written Written(Post post, IReadOnlyDictionary<string, Post> roots, ViewableTangents.Met tangents,
        IReadOnlyDictionary<string, Render.Person> people, string baseAddress)
    {
        var root = post.ParentId is null ? post : roots.GetValueOrDefault(post.TopicId);
        var tangent = tangents[post.TangentId];
        var slug = root?.Slug ?? post.TopicId;
        return new ProfilePage.Written(
            post.Id,
            post.ParentId is null ? post.Kind.ToString().ToLowerInvariant() : "reply",
            root?.Title ?? "",
            tangent.Name,
            baseAddress + "/t/" + Uri.EscapeDataString(tangent.Id),
            post.ParentId is null ? PostWrites.TopicAddress(baseAddress, tangent.Id, slug) : PostWrites.Address(baseAddress, tangent.Id, slug, post.Id),
            TangentPage.Opening(post, people),
            post.CreatedAt,
            TangentPage.EditedOf(post, people));
    }

    /// <summary>The signed-in reader as the account menu shows them, signing out back to this profile, with the place's
    /// settings where they may change them and how many things are for them and new.</summary>
    private static Who Reader(Participant reader, Capabilities place, string baseAddress, string back, Events.Unread.Badge? forYou)
        => Who.Of(Render.Person.Of(reader, baseAddress), Roles.Word(place.RoleIds), baseAddress + AuthConstants.Routes.Logout + "?return=" + back,
            TangentSpaceSettingsController.Entry(place, baseAddress), forYou);
}
