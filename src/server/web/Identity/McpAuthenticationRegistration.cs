using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tangent.Activity;

namespace Tangent.Identity;

public static class McpAuthenticationRegistration
{
    /// <summary>Registers service-proof enrollment. Call after the participation registrations
    /// (Arrival, PolicyGate) and the space and enrollment options are present.</summary>
    public static IServiceCollection AddTangentMcpAuthentication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ProofAudience>();
        services.TryAddSingleton<IServiceProofKeySource, DidDocumentKeySource>();
        services.TryAddSingleton<ServiceProofAuthentication>();
        services.TryAddSingleton<ServiceProofExchange>();
        return services;
    }
}
