using Koan.Data.Core.Commands;
using Microsoft.Extensions.Options;
using Tangent.Participants;

namespace Tangent.Space;

/// <summary>What every start makes sure of about an established place. A configured operator matters only before
/// setup, so the place starts whatever <c>Tangent:Operator</c> names; a value that names someone other than the
/// operator, by the DID of the account they are bound to or by its handle, draws one warning saying so. The place has its moderators' Tangent, which the start creates where the place
/// was established without one.</summary>
public static class FirstRunStart
{
    public static async Task Run(IServiceProvider services, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(FirstRunStart));
        var options = services.GetRequiredService<IOptions<TangentSpaceOptions>>().Value;
        if (options.NamedOperator() is { } named && space.OperatorId is { } operatorId
            && await Participant.Get(operatorId, ct) is var operatorRow && !options.MaySetUp(operatorRow?.Did ?? "", operatorRow?.Handle ?? ""))
            logger.LogWarning(
                "{Setting} names {Named}, but this Tangent Space's operator is {Operator}; the setting matters only before setup, so this start ignores it",
                FirstRun.OperatorSetting, named, operatorId);

        if (space.ModeratorsTangentId is not null) return;
        switch (await new CreateModeratorsTangent().Send(ct))
        {
            case Outcome<Tangents.Tangent>.Done { Result: var tangent }:
                logger.LogInformation("This Tangent Space had no moderators' Tangent; this start created {Tangent}", tangent.Id);
                break;
            case var other:
                logger.LogError("This Tangent Space has no moderators' Tangent, and this start could not create one: {Outcome}", other);
                break;
        }
    }
}
