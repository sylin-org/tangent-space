using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>Where a participant stands with a private Tangent: <c>in</c>, <c>asked</c> and waiting, or
/// <c>declined</c>.</summary>
public sealed record Joined(Tangent Tangent, string State)
{
    public const string InState = "in";
    public const string AskedState = "asked";
    public const string DeclinedState = "declined";
}

/// <summary>A participant joins the Tangent <see cref="Slug"/>, or asks to, as its joining says. They take part in the
/// place (<see cref="Bit.Post"/> there), or are refused with its gate before any Tangent is looked for; a Tangent they read
/// already leaves them as they are; one whose joining is closed, one that keeps them out (<see cref="Inside.Out"/>), or one
/// that no row has, is absent to them. Joining lets them in at once (<see cref="Inside.LetIn"/>), whatever they asked
/// before, and ends that ask; asking writes their <see cref="Ask"/>, once, and a declined ask is not made again.</summary>
public sealed record JoinTangent(string Slug, string ActorId, DateTimeOffset? At = null) : ICommand<Joined>
{
    public async Task<Outcome<Joined>> Run(CancellationToken ct)
    {
        var place = await Capabilities.Load(ActorId, null, null, ct);
        if (place.Gate(Bit.Post) is { } gate) return new Outcome<Joined>.Refused(gate);
        if (await Tangent.Get(Slug, ct) is not { } tangent) return new Outcome<Joined>.Refused(ChangeTangent.NoTangent);
        var here = await Capabilities.Load(ActorId, Slug, null, ct);
        if (here.Exists) return new Outcome<Joined>.Done(new Joined(tangent, Joined.InState));
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct);
        if (!Inside.Joinable(tangent, space?.ModeratorsTangentId) || Inside.Out(await Inside.Own(Slug, ActorId, ct)))
            return new Outcome<Joined>.Refused(ChangeTangent.NoTangent);

        if (tangent.Joining == Joining.Join)
        {
            await Inside.LetIn(RoleSettings.Collection(), Slug, ActorId, ct);
            if (await Ask.Get(Ask.Key(Slug, ActorId), ct) is { } before) await Ask.Remove(before.Id, ct);
            return new Outcome<Joined>.Done(new Joined(tangent, Joined.InState));
        }

        if (await Ask.Get(Ask.Key(Slug, ActorId), ct) is { } asked)
            return asked.Declined
                ? new Outcome<Joined>.Refused(Inside.AskDeclined)
                : new Outcome<Joined>.Done(new Joined(tangent, Joined.AskedState));
        await new Ask { Id = Ask.Key(Slug, ActorId), TangentId = Slug, PersonId = ActorId, AskedAt = At ?? DateTimeOffset.UtcNow }.Save(ct);
        return new Outcome<Joined>.Done(new Joined(tangent, Joined.AskedState));
    }
}

/// <summary>A participant leaves the Tangent <see cref="Slug"/>: their own overwrite there is removed, or their waiting
/// ask withdrawn; where they have neither, the command answers <see cref="Inside.NotIn"/>. Someone the place does not exist
/// for (<see cref="Bit.View"/> there) is refused with its gate before any Tangent is looked for.</summary>
public sealed record LeaveTangent(string Slug, string ActorId) : ICommand<Tangent>
{
    public async Task<Outcome<Tangent>> Run(CancellationToken ct)
    {
        if (await Capabilities.Load(ActorId, null, null, ct) is { Exists: false } place) return new Outcome<Tangent>.Refused(place.Gate(Bit.View)!);
        if (await Tangent.Get(Slug, ct) is not { } tangent) return new Outcome<Tangent>.Refused(ChangeTangent.NoTangent);
        if (Inside.In(await Inside.Own(Slug, ActorId, ct)))
        {
            await Inside.Remove(RoleSettings.Collection(), Slug, ActorId, ct);
            return new Outcome<Tangent>.Done(tangent);
        }

        if (await Ask.Get(Ask.Key(Slug, ActorId), ct) is { Declined: false })
        {
            await Ask.Remove(Ask.Key(Slug, ActorId), ct);
            return new Outcome<Tangent>.Done(tangent);
        }

        return new Outcome<Tangent>.Refused(Inside.NotIn);
    }
}

/// <summary>Whoever manages the Tangent <see cref="Slug"/> lets the participant <see cref="PersonId"/> in, or keeps them
/// out. The actor holds <see cref="Bit.ManageTangent"/> there, or is refused with its gate; the moderators' Tangent keeps
/// its access (<see cref="ChangeTangent.Fixed"/>); the participant has arrived (<see cref="Inside.NoOne"/>). Letting in writes
/// their own overwrite and ends their ask, replacing any overwrite that kept them out; keeping out declines a waiting ask,
/// or else writes their own overwrite denying <see cref="Bit.View"/> (<see cref="Inside.KeepOut"/>), which wins over their
/// roles, only where it keeps them out (<see cref="Inside.NotKept"/>): not the actor, not in a public Tangent, and not
/// someone who administers. Letting in someone not in yet, whether or not they asked, and declining a waiting ask each
/// append one <see cref="EventKind.TangentLet"/> with the participant as its target, so their door tells them.</summary>
public sealed record LetIn(string Slug, string ActorId, string PersonId, bool In, DateTimeOffset? At = null) : ICommand<Tangent>
{
    public async Task<Outcome<Tangent>> Run(CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is not { } tangent) return new Outcome<Tangent>.Refused(ChangeTangent.NoTangent);
        var capabilities = await Capabilities.Load(ActorId, Slug, null, ct);
        if (!capabilities.Exists) return new Outcome<Tangent>.Refused(ChangeTangent.NoTangent);
        if (capabilities.Gate(Bit.ManageTangent) is { } gate) return new Outcome<Tangent>.Refused(gate);
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is { } space && space.ModeratorsTangentId == Slug)
            return new Outcome<Tangent>.Refused(ChangeTangent.Fixed);
        if (string.IsNullOrWhiteSpace(PersonId) || await Participant.Get(PersonId, ct) is null) return new Outcome<Tangent>.Refused(Inside.NoOne);

        var roles = RoleSettings.Collection();
        var ask = await Ask.Get(Ask.Key(Slug, PersonId), ct);
        var now = At ?? DateTimeOffset.UtcNow;
        if (In)
        {
            var was = Inside.In(await Inside.Own(Slug, PersonId, ct));
            await Inside.LetIn(roles, Slug, PersonId, ct);
            if (ask is not null) await Ask.Remove(ask.Id, ct);
            if (!was) await Event.Append(EventKind.TangentLet, now, ct, tangentId: Slug, actorId: ActorId, targetId: PersonId, given: true);
        }
        else if (ask is { Declined: false })
        {
            ask.Declined = true;
            await ask.Save(ct);
            await Event.Append(EventKind.TangentLet, now, ct, tangentId: Slug, actorId: ActorId, targetId: PersonId, given: false);
        }
        else
        {
            var administers = (await Capabilities.Load(PersonId, null, null, ct)).Bag.Contains(RoleTokens.Administrator);
            if (Inside.NotKept(await Overwrites.AudienceOf(Slug, ct), PersonId == ActorId, administers) is { } kept)
                return new Outcome<Tangent>.Refused(kept);
            await Inside.KeepOut(roles, Slug, PersonId, ct);
        }

        return new Outcome<Tangent>.Done(tangent);
    }
}

/// <summary>Whoever manages the Tangent <see cref="Slug"/> sets how a participant outside it comes in
/// (<see cref="Joining"/>), which a public Tangent keeps for when it is private; the moderators' Tangent keeps its
/// access.</summary>
public sealed record ChangeJoining(string Slug, string ActorId, Joining Joining) : ICommand<Tangent>
{
    public async Task<Outcome<Tangent>> Run(CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is not { } tangent) return new Outcome<Tangent>.Refused(ChangeTangent.NoTangent);
        var capabilities = await Capabilities.Load(ActorId, Slug, null, ct);
        if (!capabilities.Exists) return new Outcome<Tangent>.Refused(ChangeTangent.NoTangent);
        if (capabilities.Gate(Bit.ManageTangent) is { } gate) return new Outcome<Tangent>.Refused(gate);
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is { } space && space.ModeratorsTangentId == Slug)
            return new Outcome<Tangent>.Refused(ChangeTangent.Fixed);
        if (tangent.Joining == Joining) return new Outcome<Tangent>.Done(tangent);
        tangent.Joining = Joining;
        await tangent.Save(ct);
        return new Outcome<Tangent>.Done(tangent);
    }
}
