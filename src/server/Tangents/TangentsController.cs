using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Querying;
using Koan.Identity.Roles;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Koan.Web.Hosting;
using Koan.Web.Queries;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>What creating a Tangent takes beside its name and description: its default kind, as <c>kind</c>, and its
/// audience.</summary>
public sealed record NewTangentInput(
    [Description(Holds.TangentKind)] string? Kind = null,
    [Description(Holds.TangentAudience)] string? Audience = null);

/// <summary>What changing a Tangent takes beside what it writes of its row: its default kind, as <c>kind</c>, its audience
/// and its move.</summary>
public sealed record TangentChangeInput(
    [Description(Holds.ChangedTangentKind)] string? Kind = null,
    [Description(Holds.ChangedTangentAudience)] string? Audience = null,
    [Description(Holds.TangentMove)] string? Move = null);

/// <summary>What deleting a Tangent takes.</summary>
public sealed record TangentDeleteInput([Description(Holds.DeletedTopics)] int? Topics = null);

/// <summary>The Tangents at <c>/t</c>, on Koan's entity endpoint, serving only what it declares; each operation is its route and
/// its tool, judged by the Doorway before anything is bound, and writes inside one hold with what its hooks write
/// (<see cref="TangentWriting"/>), a refusal leaving nothing written.
/// <list type="bullet">
/// <item><c>GET /t</c> is every Tangent the reader may view, in the Tangents' order, a page of <see cref="PageSize"/> read at a
/// time and continued by its sealed <c>after</c>, and, for a participant who takes part in the place, the members-only ones they
/// may join or ask to join, each with what it is about and the form that does it (<see cref="Outside"/>): one resource both ways
/// (<see cref="TangentsPage"/>), judged from Koan's compiled state and read on past what the reader is not shown, so a page is
/// full and says nothing of what it leaves out (<see cref="Listing"/>).</item>
/// <item><c>GET /t/new</c> is the form that creates one (<see cref="NewTangentPage"/>), at the segment no Tangent's slug is
/// (<see cref="Tangent.NewSegment"/>).</item>
/// <item><c>POST /t</c> creates one from its name and description and the default kind (<c>kind</c>) and <c>audience</c> it
/// takes: its slug derives from the name, the first numbered suffix of it where another Tangent holds it, and it comes after the existing
/// ones.</item>
/// <item><c>PATCH /t/{tangent}</c> changes what it names of the Tangent's name, description, accent, image and joining, and the
/// default kind (<c>kind</c>), <c>audience</c> and <c>move</c> it takes.</item>
/// <item><c>DELETE /t/{tangent}</c> deletes it whole, with the <c>topics</c> it says go with it.</item>
/// </list>
/// A page's form posts to the same addresses, naming <c>PATCH</c> or <c>DELETE</c> where it changes or deletes; a write that
/// committed sends a form on to the Tangent, to its settings after a move, and to the Tangents after a delete, and answers JSON
/// with the Tangent; a change that closed the Tangent to its own sender reads, both ways, that it is saved and no longer shows for
/// them (<see cref="TangentClosed"/>); a refused form reads its page again, keeping what was sent, with the reason where it
/// belongs.</summary>
[Route(Route)]
[Declared]
public sealed class TangentsController(KoanWebBaseUrl baseUrl) : EntityController<Tangent>, IModelHook<Tangent>, IEmitHook<Tangent>
{
    public const string Route = "/t";

    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    /// <summary>The most Tangents one page lists.</summary>
    public const int PageSize = 50;

    /// <summary>The answer when every slug the name yields belongs to another Tangent.</summary>
    public static readonly Reason NoSlug = Reason.Invalid(TangentWrites.NameField, "Every address this name gives belongs to another Tangent; choose another name.");

    /// <summary>The slug of a name with no letter or digit.</summary>
    public const string Unnamed = "tangent";

    /// <summary>The address under the base where the form that creates a Tangent is.</summary>
    public const string NewRoute = Route + "/" + Tangent.NewSegment;

    /// <summary>The Tangents' order, as the list's <see cref="PaginationAttribute.DefaultSort"/> declares it: by order, then
    /// slug.</summary>
    private static readonly Keyset<Tangent> Ordered = Keyset<Tangent>.By(nameof(Tangent.Order));

    private enum Writing { Listing, Opening, Creating, Changing, Deleting }

    private Writing _writing;

    /// <summary>Where a committed write sends its form on to, under the base, where it is not the row's own address.</summary>
    private string? _onward;

    /// <summary>What a change that closed the Tangent to its own sender answers them, where it did.</summary>
    private TangentClosed? _closed;

    [HttpGet("")]
    [HttpHead("")]
    [Tool(Name = "read_tangents")]
    [Description("List the Tangents you may read, a page at a time, and the members-only ones you may join or ask to join, which carry joining. Then call read_tangent on one's slug, start_topic in it, or join_tangent.")]
    [Needs(Bit.View)]
    [Pagination(DefaultSize = PageSize, MaxSize = PageSize, DefaultSort = nameof(Tangent.Order))]
    public override Task<IActionResult> GetCollection(CancellationToken ct)
    {
        _writing = Writing.Listing;
        if (PostWrites.PersonId(User) is not null) Response.Headers.CacheControl = "private";
        return base.GetCollection(ct);
    }

    [HttpGet(Tangent.NewSegment)]
    [Needs(Bit.View)]
    public override Task<IActionResult> GetNew(CancellationToken ct)
    {
        _writing = Writing.Opening;
        Response.Headers.CacheControl = "no-store";
        return base.GetNew(ct);
    }

    [HttpPost("")]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "create_tangent", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Needs(Bit.ManageSpace)]
    [Writes(nameof(Tangent.Name), nameof(Tangent.Description))]
    [Input<NewTangentInput>]
    [Description("Create a Tangent by naming it, for whoever holds ManageSpace. Its address derives from the name. Sending it again makes a second one. Then call start_topic.")]
    public override async Task<IActionResult> Upsert(Tangent model, CancellationToken ct)
    {
        _writing = Writing.Creating;
        var answer = await base.Upsert(model, ct);
        return await Kept(answer, async (problem, form) =>
        {
            var page = await NewPage(ct) with
            {
                Name = form[TangentWrites.NameField].ToString(),
                Description = form[TangentWrites.DescriptionField].ToString(),
                Kind = Sent(form[TangentWrites.KindField], TangentWrites.Kinds, PostWrites.Word(PostKind.Topic)),
                Audience = Sent(form[TangentWrites.AudienceField], TangentWrites.Audiences, TangentWrites.Word(Audience.Public))
            };
            return Concerning(problem) switch
            {
                TangentWrites.NameField => page with { NameFailure = problem.Detail },
                TangentWrites.DescriptionField => page with { DescriptionFailure = problem.Detail },
                null when problem.Kind == "gated" => page with { Gate = problem.Word, Create = null },
                _ => page with { Failure = problem.Detail }
            };
        });
    }

    [HttpPatch("{" + Doorway.TangentValue + "}")]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_tangent", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Needs(Bit.ManageTangent)]
    [Managing]
    [Writes(nameof(Tangent.Name), nameof(Tangent.Description), nameof(Tangent.Accent), nameof(Tangent.Image), nameof(Tangent.Joining))]
    [Input<TangentChangeInput>]
    [Description("Change a Tangent's name, description, default kind, audience, accent, image or joining (how someone outside a private Tangent comes in), or move it among the Tangents, for whoever holds ManageTangent there or ManageSpace. What you send replaces what stood, what you leave out stays, and sending a move again moves it again.")]
    public override async Task<IActionResult> Patch([Description(Holds.Tangent)] string tangent, CancellationToken ct)
    {
        _writing = Writing.Changing;
        var answer = await base.Patch(tangent, ct);
        if (_closed is { } closed) return Ok(closed);
        return await Kept(answer, async (problem, form) =>
        {
            var page = await TangentSettingsController.Compose(Here.Of(HttpContext), baseUrl.Resolve(HttpContext), baseUrl.PathBase, ct);
            page = page with
            {
                Name = form.TryGetValue(TangentWrites.NameField, out var name) ? name.ToString() : page.Name,
                Description = form.TryGetValue(TangentWrites.DescriptionField, out var description) ? description.ToString() : page.Description,
                Accent = form[TangentWrites.AccentField].ToString() is var accent && (accent.Length == 0 || Tile.Tones.Contains(accent))
                    && form.ContainsKey(TangentWrites.AccentField) ? accent : page.Accent,
                Image = Art.Judged(form[TangentWrites.ImageField].ToString()) is { } image && form.ContainsKey(TangentWrites.ImageField) ? image : page.Image,
                Kind = Sent(form[TangentWrites.KindField], TangentWrites.Kinds, page.Kind),
                Audience = Sent(form[TangentWrites.AudienceField], TangentWrites.Audiences, page.Audience)
            };
            return Concerning(problem) switch
            {
                TangentWrites.NameField => page with { NameFailure = problem.Detail },
                TangentWrites.DescriptionField => page with { DescriptionFailure = problem.Detail },
                null when problem.Kind == "gated" => page with { Gate = problem.Word, Change = null },
                _ => page with { Failure = problem.Detail }
            };
        });
    }

    [HttpDelete("{" + Doorway.TangentValue + "}")]
    [Tool(Name = "delete_tangent", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.ManageSpace, Place = true)]
    [Managing]
    [Input<TangentDeleteInput>]
    [Description("Delete a Tangent and everything in it, for whoever holds ManageSpace: its Topics and their replies go for good and no tombstone stays, and copies of its articles on Bluesky are taken down wherever their authors’ accounts still let this place. An empty Tangent goes as it is; one that holds Topics goes only when topics says how many, as read_tangent_settings counts them, and /settings/export/{tangent}.zip keeps a copy first. The moderators’ Tangent stays.")]
    public override async Task<IActionResult> Delete([Description(Holds.Tangent)] string tangent, CancellationToken ct)
    {
        _writing = Writing.Deleting;
        var answer = await base.Delete(tangent, ct);
        return await Kept(answer, async (problem, _) =>
        {
            var page = await TangentSettingsController.Compose(Here.Of(HttpContext), baseUrl.Resolve(HttpContext), baseUrl.PathBase, ct);
            return page.Delete is null ? null : page with { DeleteFailure = problem.Detail };
        });
    }

    /// <summary>A create opens the Tangent: its slug from its name, its default kind, <c>topic</c> unless it names another, its
    /// place after the others, its audience and the row of the log that begins it. A change takes the default kind it names and
    /// writes what changing a Tangent writes beside its row, or, where nothing changes, stops with
    /// the row as it stands, so nothing is written.</summary>
    public async Task OnBeforeSave(HookContext<Tangent> ctx, Tangent model)
    {
        var personId = PostWrites.PersonId(User) ?? "";
        switch (_writing)
        {
            case Writing.Creating:
                var opening = ctx.Input<NewTangentInput>();
                model.DefaultKind = TangentWrites.Chosen<PostKind>(opening?.Kind, TangentWrites.Kinds, TangentWrites.KindRule) ?? PostKind.Topic;
                var audience = TangentWrites.Chosen<Audience>(opening?.Audience, TangentWrites.Audiences, TangentWrites.AudienceRule);
                model.Name = (model.Name ?? "").Trim();
                model.Description ??= "";
                model.Id = await Tangent.FreeSlug(Slug.From(model.Name, Unnamed), ctx.Ct) ?? throw new Refusal(NoSlug);
                model.Order = await Tangent.NextOrder(ctx.Ct);
                await TangentWriting.Opened(model, audience ?? Audience.Public, personId, DateTimeOffset.UtcNow, ctx.Ct);
                break;
            case Writing.Changing:
                var input = ctx.Input<TangentChangeInput>();
                if (TangentWrites.Chosen<PostKind>(input?.Kind, TangentWrites.Kinds, TangentWrites.KindRule) is { } kind) model.DefaultKind = kind;
                var wanted = TangentWrites.Chosen<Audience>(input?.Audience, TangentWrites.Audiences, TangentWrites.AudienceRule);
                var move = TangentWrites.Chosen<Move>(input?.Move, TangentWrites.Moves, TangentWrites.MoveRule);
                if (!await TangentWriting.Changed(ctx.Prior!, model, wanted, move, personId, DateTimeOffset.UtcNow, ctx.Ct)) ctx.ShortCircuit(ctx.Prior!);
                if (move is not null) _onward = Route + "/" + Uri.EscapeDataString(model.Id) + "/" + TangentWrites.SettingsSegment;
                break;
        }
    }

    /// <summary>A change is judged here, with nothing held: the words it takes are its words, or it is refused naming its field;
    /// and a move arranges the place's list, so it needs <see cref="Bit.ManageSpace"/> in the place beside what the Doorway
    /// judged, as only the input says whether it is asked.</summary>
    public async Task OnBeforePatch(HookContext<Tangent> ctx, string id, object patch)
    {
        var input = ctx.Input<TangentChangeInput>();
        TangentWrites.Chosen<PostKind>(input?.Kind, TangentWrites.Kinds, TangentWrites.KindRule);
        TangentWrites.Chosen<Audience>(input?.Audience, TangentWrites.Audiences, TangentWrites.AudienceRule);
        if (TangentWrites.Chosen<Move>(input?.Move, TangentWrites.Moves, TangentWrites.MoveRule) is null) return;
        if ((await Capabilities.Load(PostWrites.PersonId(User), null, null, ctx.Ct)).Gate(Bit.ManageSpace) is { } arranging) throw new Refusal(arranging);
    }

    /// <summary>A delete takes everything of the Tangent with it, judged against the row read in the hold.</summary>
    public Task OnBeforeDelete(HookContext<Tangent> ctx, Tangent model)
        => TangentWriting.Deleting(model, ctx.Input<TangentDeleteInput>()?.Topics ?? 0, PostWrites.PersonId(User) ?? "", DateTimeOffset.UtcNow, ctx.Ct);

    public async Task<EmitDecision> OnEmitModel(HookContext<Tangent> ctx, object payload)
    {
        var baseAddress = baseUrl.Resolve(HttpContext);
        var personId = PostWrites.PersonId(User);
        switch (_writing)
        {
            case Writing.Opening:
                return EmitDecision.With(await NewPage(ctx.Ct));
            case Writing.Deleting when payload is Tangent deleted:
                return EmitDecision.With(new DeletedTangent(deleted.Id, deleted.Name, deleted.TopicCount, baseAddress + Route));
            case Writing.Changing when payload is Tangent closing && !(await Capabilities.Load(personId, closing.Id, null, ctx.Ct)).Exists:
                // The change closed the Tangent to its own sender: it is saved, and says nothing else of a Tangent absent to them now.
                var place = await Capabilities.Load(personId, null, null, ctx.Ct);
                _closed = new TangentClosed(closing.Name, Here.Of(HttpContext).Rows.Space.Name, baseAddress + "/",
                    await PostWrites.Reader(personId, place, baseAddress, baseUrl.PathBase + "/", ctx.Ct));
                return EmitDecision.With(_closed);
            case Writing.Creating or Writing.Changing when payload is Tangent written:
                return EmitDecision.With(await WrittenTangent.Of(written, baseAddress, personId, ctx.Ct));
            default:
                return EmitDecision.Next();
        }
    }

    public async Task<EmitDecision> OnEmitCollection(HookContext<Tangent> ctx, object payload)
        => EmitDecision.With(await Listing((payload as IEnumerable<Tangent>)?.ToList() ?? [], ctx.Next, ctx.Ct));

    protected override string AddressOf(Tangent row) => _onward ?? Route + "/" + Uri.EscapeDataString(row.Id);

    /// <summary>A page of the Tangents as the reader finds them, in the Tangents' order: those they may view, and, for a
    /// participant who takes part in the place, the members-only ones they may join or ask to join that do not keep them out
    /// (<see cref="Inside.Joinable"/>, <see cref="Inside.Out"/>), with their ask. Koan read <paramref name="rows"/>, the first
    /// rows of the page, and <paramref name="next"/> continues past them; where the reader is not shown some of them, the list
    /// is read on, <see cref="ViewableTangents.ReadSize"/> at a time, until the page holds as many as Koan read and one more is
    /// found, so a page is full, says nothing of what it leaves out, by its length or by its cursor, and has a next page only
    /// where one shows something. A page costs the Tangents read up to that one: at most every Tangent the place holds.</summary>
    private async Task<TangentsPage> Listing(IReadOnlyList<Tangent> rows, string? next, CancellationToken ct)
    {
        var here = Here.Of(HttpContext);
        var personId = here.PersonId;
        var place = here.Around;
        var space = here.Rows.Space;
        var baseAddress = baseUrl.Resolve(HttpContext);
        var protection = HttpContext.RequestServices.GetRequiredService<IDataProtectionProvider>();
        var purpose = GetType().FullName!;
        var size = rows.Count;
        var shown = await Shown(rows);
        for (var after = next is null ? null : SealedCursor.Open(protection, purpose, Ordered, next); after is not null && shown.Count <= size;)
        {
            var more = await Tangent.Page(Ordered, after, ViewableTangents.ReadSize, ct);
            shown.AddRange(await Shown(more.Items));
            after = more.Next;
        }

        var address = baseAddress + Route;
        var back = baseUrl.PathBase + Route;
        var continued = shown.Count > size ? SealedCursor.Seal(protection, purpose, Ordered, Ordered.KeyOf(shown[size - 1].Row)) : null;
        return new TangentsPage(
            shown.Take(size).Select(each => each.Entry).ToList(),
            continued is null ? null : address + "?" + After + "=" + Uri.EscapeDataString(continued),
            Request.Query.ContainsKey(After),
            address,
            place.Can,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(back) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, back, ct),
            place.Holds(Bit.ManageSpace) ? baseAddress + NewRoute : null);

        // What the reader is shown of a read's rows, in their order: one judgement from Koan's compiled state, and, for those
        // outside, one keyed read of the reader's own overwrites and one of their asks.
        async Task<List<(Tangent Row, Render.TangentEntry Entry)>> Shown(IReadOnlyList<Tangent> read)
        {
            var each = await Capabilities.LoadEach(personId, read, ct);
            var outside = personId is not null && place.Holds(Bit.Post)
                ? read.Where(row => !ViewableTangents.Views(each[row.Id]) && Inside.Joinable(row, space.ModeratorsTangentId)).ToList()
                : [];
            var keptOut = outside.Count == 0
                ? new HashSet<string>(StringComparer.Ordinal)
                : (await PermissionOverwrite.Get(outside.Select(row =>
                        PermissionOverwrite.KeyFor(Overwrites.Resource(row.Id), OverwriteSubjectKind.Person, personId!)).ToList(), ct))
                    .Where(Inside.Out).Select(own => own!.ResourceId[Overwrites.ResourcePrefix.Length..]).ToHashSet(StringComparer.Ordinal);
            var joinable = outside.Where(row => !keptOut.Contains(row.Id)).Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
            var asks = joinable.Count == 0
                ? new Dictionary<string, Ask>()
                : (await Ask.Get(joinable.Select(id => Ask.Key(id, personId!)).ToList(), ct)).OfType<Ask>().ToDictionary(ask => ask.TangentId, StringComparer.Ordinal);
            return read.Where(row => ViewableTangents.Views(each[row.Id]) || joinable.Contains(row.Id))
                .Select(row => (row, joinable.Contains(row.Id) ? Outside(row, baseAddress, asks.GetValueOrDefault(row.Id)) : Listed(row, baseAddress)))
                .ToList();
        }
    }

    /// <summary>The form that creates a Tangent, as the reader finds it: theirs to send where they hold
    /// <see cref="Bit.ManageSpace"/>, otherwise read only, with why.</summary>
    private async Task<NewTangentPage> NewPage(CancellationToken ct)
    {
        var here = Here.Of(HttpContext);
        var place = here.Around;
        var personId = here.PersonId;
        var baseAddress = baseUrl.Resolve(HttpContext);
        var back = baseUrl.PathBase + NewRoute;
        return new NewTangentPage(
            place.Gate(Bit.ManageSpace) is null ? TangentWrites.CreateForm(baseAddress + Route) : null,
            place.Can,
            place.Gate(Bit.ManageSpace)?.Word,
            "",
            "",
            PostWrites.Word(PostKind.Topic),
            TangentWrites.Word(Audience.Public),
            TangentWrites.Kinds,
            TangentWrites.Audiences,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(back) : null,
            here.Rows.Space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, back, ct));
    }

    /// <summary>A refused write as its sender reads it: a page's form reads its page again, composed by
    /// <paramref name="again"/> from the refusal and what the form sent, with the refusal's status; anything else, or a form whose
    /// page offers nothing to send again, reads the refusal as it is.</summary>
    private async Task<IActionResult> Kept(IActionResult answer, Func<Problem, IFormCollection, Task<object?>> again)
    {
        if (answer is not ObjectResult { Value: Problem problem } || !Request.HasFormContentType || Answers.WantsJson(Request)) return answer;
        Response.Headers.CacheControl = "no-store";
        return await again(problem, await Request.ReadFormAsync()) is { } page ? new ObjectResult(page) { StatusCode = problem.Status } : answer;
    }

    /// <summary>The field a refusal concerns, the one it names; null where it names none.</summary>
    private static string? Concerning(Problem problem) => problem.Errors?.Keys.FirstOrDefault();

    /// <summary>The word a field sent where it is one of <paramref name="words"/>, else <paramref name="otherwise"/>.</summary>
    private static string Sent(string? word, IReadOnlyList<string> words, string otherwise)
        => TangentWrites.Choice(word, words, out _) is { } index ? words[index] : otherwise;

    /// <summary>A members-only Tangent as someone outside it finds it listed: what it is about, how they come in, and the
    /// form that joins or asks, or that withdraws their waiting ask. Where anyone joins, an ask they made before, waiting or
    /// declined, says nothing, and they join. Its tile carries no picture brought in here, which reaches only those who may
    /// view the Tangent (<see cref="MediaUse.Reaches"/>); a cover reaches anyone and stays.</summary>
    public static Render.TangentEntry Outside(Tangent tangent, string baseAddress, Ask? asked)
    {
        var address = PostWrites.TangentAddress(baseAddress, tangent.Id);
        var ask = tangent.Joining == Joining.Join ? null : asked;
        var joining = ask is null ? tangent.Joining == Joining.Join ? "join" : "ask" : ask.Declined ? Joined.DeclinedState : Joined.AskedState;
        var listed = Listed(tangent, baseAddress);
        return listed with
        {
            Tile = Media.IdIn(tangent.Image) is null ? listed.Tile : listed.Tile! with { Image = null },
            Description = tangent.Description,
            Joining = joining,
            Join = ask is null
                ? Form.Posting(address + "/" + JoinController.JoinSegment) with { Tool = "join-tangent", Submit = joining == "join" ? "Join" : "Ask to join", Quiet = true }
                : null,
            Withdraw = ask is { Declined: false } ? Form.Posting(address + "/" + JoinController.LeaveSegment) with { Tool = "withdraw-ask", Submit = "Withdraw", Quiet = true } : null
        };
    }

    /// <summary>A Tangent as a list of them shows it: its slug, name, kind, address, the Topics started there and the tile it
    /// chose.</summary>
    public static Render.TangentEntry Listed(Tangent tangent, string baseAddress)
        => Render.TangentEntry.Of(tangent, PostWrites.TangentAddress(baseAddress, tangent.Id), baseAddress + "/");
}
