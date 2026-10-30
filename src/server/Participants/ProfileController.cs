using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Identity.Roles;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tangent.Access;
using Tangent.Delegates;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;
using KoanPerson = Koan.Identity.Identity;

namespace Tangent.Participants;

/// <summary>A participant's profile, one resource both ways, at <c>/p/{handle}</c>: the profile their PDS gave, their
/// kind, the roles they hold here, when they arrived, and their recent Posts in the Tangents the reader may view, left to
/// the formatter as HTML, a fragment or JSON. A reader without <see cref="Bit.View"/> on the place receives its gate, the
/// same for every handle, held or not; a handle no participant holds now answers as a missing resource. The address
/// takes a handle, matched as the participant's row keeps it, or a participant's DID. Every read is bounded and keyed:
/// capability's rows for the place; the participant by handle or Id; Koan's roles of theirs and their person's row; the
/// Tangents the reader may view (<see cref="ViewableTangents.All"/>), every Tangent judged from Koan's compiled state, so
/// as many as the place holds; one page of their Posts in those (<see cref="RecentPosts"/>); the roots of the replies
/// among them; the participants those Posts mention; and, on the reader's own profile in their own session, a page of
/// their approvals. A revoke posts back here and is the reader's alone. For a reader who holds <see cref="Bit.ManageStanding"/>
/// or <see cref="Bit.ManageRoles"/>, it also reads the participant as the place's people list them, with what the reader
/// may change of them and the forms that do, which post to <see cref="PeopleController"/> and come back here.</summary>
public sealed class ProfileController(KoanWebBaseUrl baseUrl, RoleCollection roles, IOptions<AuthServerOptions> server) : ControllerBase
{
    /// <summary>The query parameter that carries the cursor of the page of Posts to continue from.</summary>
    public const string Before = "before";

    /// <summary>The query parameter that carries the page of the reader's own approvals, from one.</summary>
    public const string AgentsPage = "agents";

    /// <summary>The segment under the reader's own profile where an approval is revoked: <c>…/delegates/{id}/revoke</c>.</summary>
    public const string DelegatesSegment = "delegates";

    public const string RevokeSegment = "revoke";

    [HttpGet(ProfilePage.Segment + "{handle}")]
    [HttpHead(ProfilePage.Segment + "{handle}")]
    public async Task<IActionResult> Get(string handle, [FromQuery(Name = Before)] string? before, [FromQuery(Name = AgentsPage)] string? agents,
        CancellationToken ct)
    {
        var personId = PostWrites.PersonId(User);
        var rows = await Capabilities.LoadRows(personId, null, ct);
        var place = rows.For(null);
        if (!place.Exists) return DoorController.Gated(place.Gate(Bit.View) ?? Capabilities.Absent);
        if (await Holder(handle, ct) is not { } subject) return PostWrites.Absent();

        RecentPosts.Cursor? cursor = null;
        if (before is not null && (cursor = RecentPosts.Cursor.Parse(before)) is null)
            return new Problem(StatusCodes.Status400BadRequest, "Not a page of these posts",
                $"The {Before} parameter is the cursor a page of this profile names as its older.").ToResult();
        var agentsPage = 1;
        if (agents is not null && (!int.TryParse(agents, NumberStyles.None, CultureInfo.InvariantCulture, out agentsPage)
                                   || agentsPage is < 1 or > Approved.LastPage))
            return new Problem(StatusCodes.Status400BadRequest, "Not a page of these agents",
                $"The {AgentsPage} parameter is the number of a page of the agents acting as you, from 1.").ToResult();

        var whole = await ViewableTangents.All(personId, ct);
        var tangents = whole.Tangents.ToDictionary(tangent => tangent.Id, StringComparer.Ordinal);
        var page = await RecentPosts.Read(subject.Id, tangents.Keys.ToHashSet(StringComparer.Ordinal), whole.Others, cursor, ct);
        var roots = await Roots(page.Posts, ct);
        var people = await Mentioned(page.Posts, ct);
        var held = (await roles.ForPerson(subject.Id, ct)).Where(role => role.Key != Roles.Everyone).Select(role => role.Name)
            .Order(StringComparer.Ordinal).ToList();
        var person = await KoanPerson.Get(subject.Id, ct);

        var baseAddress = BaseAddress();
        var (name, shownHandle) = Named(subject);
        var address = ProfilePage.AddressOf(baseAddress, shownHandle);
        var reader = rows.Participant;
        var own = reader is not null && reader.Id == subject.Id && !DelegateIdentity.Delegated(HttpContext);
        ProfilePage.Approvals? approvals = null;
        if (own)
        {
            var (grants, more) = await Approved.Of(reader!.Id, agentsPage, ct);
            approvals = new ProfilePage.Approvals(
                grants.Select(grant => new ProfilePage.Approval(grant.Id, Approved.Client(grant), grant.ClientId != DelegateClient.Id, grant.IssuedAt,
                    address + "/" + DelegatesSegment + "/" + Uri.EscapeDataString(grant.Id) + "/" + RevokeSegment)).ToList(),
                server.Value.AccessTokenLifetime,
                FormToken.For(HttpContext),
                more ? address + "?" + AgentsPage + "=" + (agentsPage + 1).ToString(CultureInfo.InvariantCulture) : null,
                agentsPage > 1 ? address : null);
            // The reader's own profile carries the revoke forms with a fresh antiforgery token, so it is never stored or
            // revalidated; anyone else's, read signed in, is theirs alone to cache.
            Response.Headers.CacheControl = "no-cache, no-store";
        }
        else if (reader is not null)
        {
            Response.Headers.CacheControl = "private";
        }

        // Whoever may change the participant's standing or roles reads them as the people list them, with the forms that
        // change them, coming back here; those forms carry a fresh antiforgery token, so the page is never stored.
        PeoplePage.Listed? listed = null;
        PeopleForms? forms = null;
        if (reader is not null && PeopleController.ReadGate(place) is null)
        {
            listed = await (await PeopleController.Lister.Load(roles, place, reader.Id, baseAddress, ct)).Row(subject, ct);
            forms = PeopleController.Forms(HttpContext, baseAddress, baseUrl.PathBase + ProfilePage.Segment + Uri.EscapeDataString(shownHandle));
            Response.Headers.CacheControl = "no-cache, no-store";
        }

        var back = Uri.EscapeDataString(baseUrl.PathBase + ProfilePage.Segment + shownHandle);
        return Ok(new ProfilePage(
            subject.Id,
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
            place.Gate(Bit.Post),
            page.Posts.Select(post => Written(post, roots, tangents, people, baseAddress)).ToList(),
            page.Next is { } next ? address + "?" + Before + "=" + Uri.EscapeDataString(next.ToString()) : null,
            cursor is not null,
            approvals,
            rows.Space.Name,
            baseAddress + "/",
            reader is null ? baseAddress + SignInController.Path + "?return=" + back : null,
            reader is null ? null : Reader(reader, place, back, await Events.Notifications.Count(reader.Id, baseAddress, ct)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            listed,
            forms));
    }

    /// <summary>Revokes one of the reader's own approvals and sends them back to their profile, or answers <c>204</c> as
    /// JSON. Only the person does it, in their own session with the form's token: a delegate's bearer is refused, and
    /// a profile that is not the reader's own, or an approval that is not theirs or no longer stands, is answered as the
    /// revoke is, with nothing changed.</summary>
    [HttpPost(ProfilePage.Segment + "{handle}/" + DelegatesSegment + "/{grant}/" + RevokeSegment)]
    public async Task<IActionResult> Revoke(string handle, string grant, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (DelegateIdentity.Delegated(HttpContext))
            return new Problem(StatusCodes.Status403Forbidden, "Only the person revokes",
                "A request made with a bearer token can’t revoke an agent; the person signs in and revokes it on their profile.").ToResult();
        if (PostWrites.PersonId(User) is not { } personId)
            return new Problem(StatusCodes.Status401Unauthorized, "Sign in first", "Revoking an agent needs the person who approved it, signed in.",
                Capabilities.SignIn).ToResult();
        if (!await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        if (await Holder(handle, ct) is not { } subject || subject.Id != personId) return PostWrites.Absent();

        await Approved.Revoke(personId, grant, ct);
        if (PostWrites.WantsJson(Request)) return NoContent();
        Response.Headers.Location = baseUrl.PathBase + ProfilePage.Segment + Uri.EscapeDataString(Named(subject).Handle);
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>The participant the address names: by Id where it is a DID; otherwise the one participant whose row keeps
    /// the handle, as written or in lower case, as atproto writes handles. A handle has one holder: the writer records a
    /// verified handle and takes it from any other row in one command (<see cref="Handles.Release"/>), so one keyed read of
    /// one row answers.</summary>
    private static async Task<Participant?> Holder(string handle, CancellationToken ct)
    {
        if (handle.Length is 0 or > Mentions.IdentityLimit) return null;
        if (handle.StartsWith("did:", StringComparison.Ordinal)) return await Participant.Get(handle, ct);
        if (handle.Length > Participants.SignIn.HandleLimit) return null;
        var query = new QueryDefinition { Page = 1, PageSize = 1 };
        var rows = await Participant.Query(participant => participant.Handle == handle, query, ct);
        var lower = handle.ToLowerInvariant();
        if (rows.Count == 0 && lower != handle) rows = await Participant.Query(participant => participant.Handle == lower, query, ct);
        return rows.FirstOrDefault();
    }

    /// <summary>The roots of the replies among the Posts, by Id, in one keyed read.</summary>
    private static async Task<IReadOnlyDictionary<string, Post>> Roots(IReadOnlyList<Post> posts, CancellationToken ct)
    {
        var ids = posts.Where(post => post.ParentId is not null).Select(post => post.TopicId).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Post>();
        return (await Post.Get(ids, ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);
    }

    /// <summary>The participants the Posts mention, as their openings read them, in one keyed read.</summary>
    private static async Task<IReadOnlyDictionary<string, TangentPage.Author>> Mentioned(IReadOnlyList<Post> posts, CancellationToken ct)
    {
        var ids = posts.SelectMany(post => post.MentionedIds).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, TangentPage.Author>();
        return (await Participant.Get(ids, ct)).OfType<Participant>().ToDictionary(row => row.Id, row =>
        {
            var (name, handle) = Named(row);
            return new TangentPage.Author(row.Id, handle, name, row.Kind.ToString().ToLowerInvariant(), row.Profile.Avatar);
        }, StringComparer.Ordinal);
    }

    private static ProfilePage.Written Written(Post post, IReadOnlyDictionary<string, Post> roots, IReadOnlyDictionary<string, Tangents.Tangent> tangents,
        IReadOnlyDictionary<string, TangentPage.Author> people, string baseAddress)
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
            post.CreatedAt);
    }

    /// <summary>The signed-in reader as the account menu shows them, signing out back to this profile, with how many of
    /// their notifications they have not seen.</summary>
    private Who Reader(Participant reader, Capabilities place, string back, Events.Notifications.Unseen unseen)
    {
        var (name, handle) = Named(reader);
        return new Who(reader.Id, name, handle, Roles.Word(place.RoleIds), reader.Profile.Avatar,
            BaseAddress() + AuthConstants.Routes.Logout + "?return=" + back, Notifications: unseen);
    }

    /// <summary>The name a participant is shown by, and their handle: the profile's display name, else the handle, else
    /// their Id.</summary>
    private static (string Name, string Handle) Named(Participant participant)
    {
        var handle = participant.Handle.Length > 0 ? participant.Handle : participant.Id;
        return (participant.Profile.DisplayName is { Length: > 0 } name ? name : handle, handle);
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
