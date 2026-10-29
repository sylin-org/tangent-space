using Koan.Identity.Roles;
using Microsoft.Extensions.Options;
using Tangent.Access;

namespace Tangent.Space;

/// <summary>What every start makes sure of about first run. An unestablished place with no operator configured draws
/// a new setup code and writes it to the log once. An established place refuses to start under a configured operator
/// other than the one it was established with, since changing the setting never transfers the place; and its operator
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
                logger.LogInformation(
                    "Setup code {SetupCode}: type it on the first-run page to set up this Tangent Space; it changes at every start until then, and setting {Setting} to your DID replaces it",
                    services.GetRequiredService<SetupCode>().Draw(), FirstRun.OperatorSetting);
            return;
        }

        if (configured is not null && configured != space.OperatorId)
            throw new InvalidOperationException(
                $"{FirstRun.OperatorSetting} names {configured}, but this Tangent Space's operator is {space.OperatorId}. Changing the setting never " +
                $"transfers the place: set {FirstRun.OperatorSetting} to {space.OperatorId}, or remove it.");

        if (space.OperatorId is not { } operatorId) return;
        var roles = services.GetRequiredService<RoleCollection>();
        if ((await roles.Get(Roles.Administrator, ct))?.Members.Contains(operatorId) == true) return;
        await roles.Add(Roles.Administrator, operatorId, ct);
        logger.LogInformation("The operator {Operator} was not a member of {Role}; this start made them one", operatorId, Roles.Administrator);
    }
}
