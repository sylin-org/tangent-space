using Koan.Identity.Roles;
using Microsoft.Extensions.Options;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>What every start makes sure of about an established place. A configured operator matters only before
/// setup, so the place starts whatever <c>Tangent:Operator</c> names; a value that names someone other than the
/// operator draws one warning saying so. The place has its moderators' Tangent, which the start creates where the place
/// was established without one; and its operator is a member of the administrator role, which the start restores when
/// a first run's own add never landed.</summary>
public static class FirstRunStart
{
    public static async Task Run(IServiceProvider services, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(FirstRunStart));
        var options = services.GetRequiredService<IOptions<TangentSpaceOptions>>().Value;
        if (options.NamedOperator() is { } named && space.OperatorId is { } operatorId
            && !options.MaySetUp(operatorId, (await Participant.Get(operatorId, ct))?.Handle ?? ""))
            logger.LogWarning(
                "{Setting} names {Named}, but this Tangent Space's operator is {Operator}; the setting matters only before setup, so this start ignores it",
                FirstRun.OperatorSetting, named, operatorId);

        if (space.ModeratorsTangentId is null) CreateModerators(services, logger);

        if (space.OperatorId is not { } administrator) return;
        var roles = services.GetRequiredService<RoleCollection>();
        if ((await roles.Bag(administrator, authenticated: true, ct)).InRole(Roles.Administrator)) return;
        await roles.Add(Roles.Administrator, administrator, ct);
        logger.LogInformation("The operator {Operator} was not a member of {Role}; this start made them one", administrator, Roles.Administrator);
    }

    /// <summary>Queues <see cref="CreateModeratorsTangent"/> as the writer's first command and logs its outcome. The
    /// writer starts with Koan's background services, beside this start rather than before it, so the start does not wait
    /// for the outcome: the command runs before any command a request sends, since the server listens only once the start
    /// is done.</summary>
    private static void CreateModerators(IServiceProvider services, ILogger logger)
    {
        _ = services.GetRequiredService<WriteLoop>().Send(new CreateModeratorsTangent(), CancellationToken.None).ContinueWith(sent =>
        {
            switch (sent.IsCompletedSuccessfully ? sent.Result : new Outcome<Tangents.Tangent>.Failed(sent.Exception!))
            {
                case Outcome<Tangents.Tangent>.Done { Result: var tangent }:
                    logger.LogInformation("This Tangent Space had no moderators' Tangent; this start created {Tangent}", tangent.Id);
                    break;
                case var other:
                    logger.LogError("This Tangent Space has no moderators' Tangent, and this start could not create one: {Outcome}", other);
                    break;
            }
        }, TaskScheduler.Default);
    }
}
