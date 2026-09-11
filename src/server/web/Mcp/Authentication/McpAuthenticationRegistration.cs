using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TangentSpace.Mcp.Authentication;

public static class McpAuthenticationRegistration
{
    /// <summary>Registers the Tangent MCP proof-exchange authentication profile. Call after the shared
    /// Spaces/participation registrations (Arrival, PolicyGate, SpacesVerifier, SpacesOptions) are present.</summary>
    public static IServiceCollection AddTangentMcpAuthentication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IServiceProofKeySource, SpacesServiceProofKeySource>();
        services.TryAddSingleton<ServiceProofAuthentication>();
        services.TryAddSingleton<ServiceProofExchange>();
        return services;
    }
}
