using System.Security.Claims;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>What a writer can name as they type, in a Tangent and a Topic of it: after <c>@</c>, the people and the roles,
/// and after <c>#</c>, the Tangents they may view, at most <see cref="Limit"/>, judged as <see cref="Bit.View"/> on the
/// Tangent. People come in the order a conversation meets them: an exact handle first, then who wrote in this Topic, who
/// wrote lately in this Tangent, and whoever's handle starts with what was typed; a role is every role the place gives
/// (<see cref="Roles.Given"/>), with how many hold it. Every read is bounded and takes no lock: the Tangent and the
/// reader's rows, the Topic's root, a page of the Tangent's Topics, the participants they name in one keyed read, one
/// range of the handle index, a page of the roles and one count each, or the Tangents the reader may view. Nothing is
/// written.</summary>
public sealed class SuggestionsController : ControllerBase
{
    /// <summary>The segment under a Tangent's address where suggestions are read; <see cref="Slug.Reserved"/> keeps it
    /// from every Topic.</summary>
    public const string Segment = "suggest";

    /// <summary>The query parameter holding what was typed, from its <c>@</c> or <c>#</c>.</summary>
    public const string Typed = "q";

    /// <summary>The query parameter naming the Topic, by slug, whose writers come first.</summary>
    public const string InTopic = "topic";

    /// <summary>The most suggestions one answer holds.</summary>
    public const int Limit = 10;

    /// <summary>The most of a Tangent's latest Topics whose writers are read.</summary>
    public const int TopicsRead = 10;

    /// <summary>The most roles read.</summary>
    public const int RolesRead = 50;

    /// <summary>Where the suggestions of the Tangent at <paramref name="tangentAddress"/> are read, with the Topic whose
    /// writers come first where there is one.</summary>
    public static string Address(string tangentAddress, string? topic = null)
        => tangentAddress + "/" + Segment + (topic is null ? "" : "?" + InTopic + "=" + Uri.EscapeDataString(topic));

    /// <summary>The props of a body field's suggest island, which reads them at <see cref="Address"/>.</summary>
    public static string Props(string tangentAddress, string? topic = null)
        => System.Text.Json.JsonSerializer.Serialize(new { address = Address(tangentAddress, topic) });

    [HttpGet("/t/{tangent}/" + Segment)]
    public async Task<IActionResult> Suggest(string tangent, [FromQuery(Name = Typed)] string? typed,
        [FromQuery(Name = InTopic)] string? topic, CancellationToken ct)
    {
        var personId = User.Identity?.IsAuthenticated == true ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
        var inTangent = await Tangents.Tangent.Get(tangent, ct);
        var rows = await Capabilities.LoadRows(personId, inTangent?.Id, ct);
        var here = rows.For(null);
        if (!here.Exists) return DoorController.Gated(HttpContext, here.Gate(Bit.View) ?? Capabilities.Absent);
        if (inTangent is not { } tangentRow) return DoorController.Gated(HttpContext, Capabilities.Absent);
        if (typed is not { Length: > 0 } || typed[0] is not ('@' or '#'))
            return new Problem(StatusCodes.Status400BadRequest, "Send what was typed from its @ or #, as q=@ad or q=#gar.").ToResult();

        var needle = typed[1..].Trim().ToLowerInvariant();
        if (needle.Length > Participants.SignIn.HandleLimit) needle = needle[..Participants.SignIn.HandleLimit];
        var root = topic is not null && Slug.IsValid(topic) ? await TopicWindow.Topic(tangentRow.Id, topic, ct) : null;
        var found = typed[0] == '#' ? await Viewable(personId, needle, ct) : await Named(personId, tangentRow.Id, root, needle, ct);
        return Ok(new Answer(found));
    }

    /// <summary>The suggestions, best first.</summary>
    public sealed record Answer(IReadOnlyList<Suggestion> Suggestions);

    /// <summary>One suggestion: whether it is a <c>person</c>, a <c>role</c> or a <c>tangent</c>; what picking it writes after
    /// the <c>@</c> or <c>#</c>; the name it is shown by; and a note beside it — a person's kind where they are an agent, how
    /// many hold a role, a Tangent's description.</summary>
    public sealed record Suggestion(string Kind, string Insert, string Name, string? Note);

    /// <summary>How well a name meets what was typed: 0 for the whole of it, 1 where it starts with it, 2 where it holds
    /// it, null where it does neither. Nothing typed meets everything at 1.</summary>
    private static int? Meets(string needle, params string?[] names)
    {
        if (needle.Length == 0) return 1;
        int? best = null;
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            var lowered = name.ToLowerInvariant();
            int? meets = lowered == needle ? 0 : lowered.StartsWith(needle, StringComparison.Ordinal) ? 1 : lowered.Contains(needle, StringComparison.Ordinal) ? 2 : null;
            if (meets < (best ?? 3)) best = meets;
        }

        return best;
    }

    private static async Task<IReadOnlyList<Suggestion>> Named(string? personId, string tangentId, Post? root, string needle, CancellationToken ct)
    {
        // Who a conversation meets, in its order: this Topic's writers, then the Tangent's latest writers, then handles.
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(IEnumerable<string?> ids)
        {
            foreach (var id in ids)
                if (id is not null && id != personId && seen.Add(id)) order.Add(id);
        }

        if (root is not null) Add(root.RecentAuthorIds.Prepend(root.AuthorId));
        foreach (var latest in (await TopicList.Read(tangentId, null, ct, TopicsRead)).Topics) Add(latest.RecentAuthorIds.Prepend(latest.AuthorId));
        var known = (await Participant.Get(order, ct)).OfType<Participant>().ToDictionary(participant => participant.Id, StringComparer.Ordinal);
        var byHandle = People.Start(needle) is { } start ? (await People.Read(start, null, ct)).Participants : [];
        foreach (var participant in byHandle) known.TryAdd(participant.Id, participant);
        Add(byHandle.Select(participant => participant.Id));

        var ranked = new List<(int Meets, int Source, Suggestion Suggestion)>();
        for (var index = 0; index < order.Count; index++)
        {
            if (!known.TryGetValue(order[index], out var participant) || participant.Handle.Length == 0) continue;
            if (participant.Standing is Standing.Banned or Standing.Applied or Standing.Declined) continue;
            if (Meets(needle, participant.Handle, participant.Profile.DisplayName) is not { } meets) continue;
            ranked.Add((meets, index, new Suggestion("person", participant.Handle,
                participant.Profile.DisplayName is { Length: > 0 } name ? name : participant.Handle,
                participant.Kind == ParticipantKind.Agent ? "agent" : null)));
        }

        var roles = AppHost.GetRequiredService<RoleCollection>(nameof(SuggestionsController));
        foreach (var role in (await roles.Page(null, 1, RolesRead, ct)).Items.Where(role => Roles.Given(role.Key)))
        {
            var word = Roles.Word(role.Key);
            if (Meets(needle, word, role.Name) is not { } meets) continue;
            var holders = await RoleMember.Count.Where(member => member.RoleKey == role.Key, CountStrategy.Exact, ct);
            ranked.Add((meets, order.Count, new Suggestion("role", word, role.Name, holders == 1 ? "1 person" : $"{holders} people")));
        }

        return ranked.OrderBy(entry => entry.Meets).ThenBy(entry => entry.Source).Take(Limit).Select(entry => entry.Suggestion).ToList();
    }

    private static async Task<IReadOnlyList<Suggestion>> Viewable(string? personId, string needle, CancellationToken ct)
        => (await ViewableTangents.All(personId, ct)).Tangents
            .Select((tangent, index) => (Meets: Meets(needle, tangent.Id, tangent.Name), Index: index, Tangent: tangent))
            .Where(entry => entry.Meets is not null)
            .OrderBy(entry => entry.Meets).ThenBy(entry => entry.Index).Take(Limit)
            .Select(entry => new Suggestion("tangent", entry.Tangent.Id, entry.Tangent.Name, null)).ToList();
}
