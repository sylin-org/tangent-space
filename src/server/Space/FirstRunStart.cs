using Koan.Identity.Roles;
using Microsoft.Extensions.Options;
using Tangent.Access;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>What every start makes sure of about first run. An unestablished place with no operator configured draws
/// a new setup code and writes it to the log once, as a warning, since the operator must act on it and ordinary
/// production filters keep warnings. An established place refuses to start under a configured operator
/// other than the one it was established with, since changing the setting never transfers the place; it has its
/// moderators' Tangent, which the start creates where the place was established without one; and its operator
/// is a member of the administrator role, which the start restores when a first run's own add never landed.</summary>
public static class FirstRunStart
{
    public static async Task Run(IServiceProvider services, CancellationToken ct)
    {
        var configured = services.GetRequiredService<IOptions<TangentSpaceOptions>>().Value.Operator;
        var logger = services.GetRequiredService<ILogger<SetupCode>>();
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct);
        if (space is null)
        {
            if (configured is null)
                logger.LogWarning(
                    "Setup code {SetupCode}: type it on the first-run page to set up this Tangent Space; it changes at every start until then, and setting {Setting} to your DID replaces it",
                    services.GetRequiredService<SetupCode>().Draw(), FirstRun.OperatorSetting);
            return;
        }

        if (configured is not null && configured != space.OperatorId)
            throw new InvalidOperationException(
                $"{FirstRun.OperatorSetting} names {configured}, but this Tangent Space's operator is {space.OperatorId}. Changing the setting never " +
                $"transfers the place: set {FirstRun.OperatorSetting} to {space.OperatorId}, or remove it.");

        if (space.ModeratorsTangentId is null) CreateModerators(services, logger);

        if (space.OperatorId is not { } operatorId) return;
        var roles = services.GetRequiredService<RoleCollection>();
        if ((await roles.Get(Roles.Administrator, ct))?.Members.Contains(operatorId) == true) return;
        await roles.Add(Roles.Administrator, operatorId, ct);
        logger.LogInformation("The operator {Operator} was not a member of {Role}; this start made them one", operatorId, Roles.Administrator);
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
